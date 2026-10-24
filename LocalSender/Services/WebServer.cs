using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace EdgeDrop;

public class WebFileOffer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Modified { get; set; } = "";
}

public class ActiveSessionInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Phone (Web)";
    public string OriginalDefaultName { get; set; } = "";
    public string Ip { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public bool IsCollapsed { get; set; } = false;
    public bool IsMobile { get; set; } = true;
}

public class WebServer : IAsyncDisposable, IDisposable
{
    private readonly int _port;
    private readonly string _saveFolder;
    private WebApplication? _app;

    public static (string Name, bool IsMobile) ParseUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua))
        {
            return ("Phone (Web)", true);
        }

        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
        {
            return ("iPhone", true);
        }

        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            return ("iPad", true);
        }

        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return ("Android", true);
        }

        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return ("Browser (Windows)", false);
        }

        if (ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) || ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase))
        {
            return ("Browser (macOS)", false);
        }

        if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            return ("Browser (Linux)", false);
        }

        if (ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
        {
            return ("Mobile Browser", true);
        }

        return ("Browser (Web)", false);
    }

    private string _pin = "";
    private readonly object _sessionsLock = new();
    private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);
    private readonly object _authAttemptsLock = new();
    private readonly Dictionary<string, (int fails, DateTime until)> _authAttempts = new();

    private readonly object _subscribersLock = new();
    private readonly List<Action<string>> _sseSubscribers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _offeredFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ActiveSessionInfo> _activeSessions = new();
    private WebFileOffer? _latestOffer;

    public event Action<string>? Uploaded;
    public event Action<string>? Error;
    public event Action? ActiveSessionsChanged;

    public event Action<string, long>? UploadStarted;
    public event Action<string, TransferProgressReport>? UploadProgress;
    public event Action<string>? UploadCompleted;
    public event Action<string>? UploadCancelled;

    public bool RequireAcceptIncoming { get; set; } = true;
    public bool SafeModeEnabled { get; set; } = true;
    public Func<string, string, long, Task<bool>>? AcceptFileRequested;
    public void ApproveClient(string clientIp, TimeSpan? duration = null)
    {
        SenderApprovalManager.Approve(clientIp, duration);
    }

    public void RevokeClientApproval(string clientIp)
    {
        SenderApprovalManager.Revoke(clientIp);
    }

    public bool IsRunning => _app != null;
    public int Port => _port;
    public string Pin => _pin;
    public bool HasActiveSessions => !_activeSessions.IsEmpty;
    public int ActiveSessionCount => _activeSessions.Count;
    public IReadOnlyList<ActiveSessionInfo> ActiveSessions => _activeSessions.Values.ToList();

    public void DisconnectSession(string sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out _))
        {
            ActiveSessionsChanged?.Invoke();
        }
    }

    public string RegeneratePin()
    {
        lock (_sessionsLock)
        {
            _sessions.Clear();
        }
        lock (_authAttemptsLock)
        {
            _authAttempts.Clear();
        }
        _activeSessions.Clear();
        ActiveSessionsChanged?.Invoke();

        _pin = Random.Shared.Next(1000, 10000).ToString();
        return _pin;
    }

    private bool IsAuthorized(HttpContext ctx)
    {
        if (ctx.Request.Cookies.TryGetValue("edgedrop_session", out var token) && !string.IsNullOrWhiteSpace(token))
        {
            lock (_sessionsLock)
            {
                return _sessions.Contains(token);
            }
        }
        return false;
    }

    public void OfferFileToPhone(string filePath)
    {
        if (!HasActiveSessions)
        {
            throw new InvalidOperationException("No active sessions (open site on mobile device)");
        }

        var fi = new FileInfo(filePath);
        if (!fi.Exists)
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        _offeredFiles[fi.Name] = filePath;

        var offer = new WebFileOffer
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = fi.Name,
            Size = fi.Length,
            Modified = fi.LastWriteTimeUtc.ToString("o")
        };
        _latestOffer = offer;

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "file-offer",
            file = offer
        });

        lock (_subscribersLock)
        {
            foreach (var sub in _sseSubscribers.ToArray())
            {
                try { sub(payload); } catch { }
            }
        }
    }

    public WebServer(int port, string saveFolder)
    {
        _port = port > 0 ? port : 8080;
        _saveFolder = string.IsNullOrWhiteSpace(saveFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "EdgeDrop")
            : saveFolder;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null) return;

        try
        {
            _pin = Random.Shared.Next(1000, 10000).ToString();
            var builder = WebApplication.CreateBuilder();

            builder.Logging.ClearProviders();

            builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = null;
                options.Limits.MaxConcurrentConnections = 50;
                options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(2);
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            });

            builder.Services.Configure<HostOptions>(options =>
            {
                options.ShutdownTimeout = TimeSpan.FromSeconds(1);
            });

            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = long.MaxValue;
                options.ValueLengthLimit = int.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            var app = builder.Build();

            app.Use(async (ctx, next) =>
            {
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                ctx.Response.Headers["X-Frame-Options"] = "DENY";
                await next();
            });

            app.Use(async (ctx, next) =>
            {
                var remoteIp = ctx.Connection.RemoteIpAddress;
                if (remoteIp != null && !IsLocalOrPrivateIp(remoteIp))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsync("Forbidden: Local network access only.");
                    return;
                }
                await next();
            });

            app.Use(async (ctx, next) =>
            {
                var hostHeader = ctx.Request.Host.Host;
                if (string.IsNullOrWhiteSpace(hostHeader))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                bool isAllowed =
                    hostHeader.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    hostHeader.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    hostHeader.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                    System.Net.IPAddress.TryParse(hostHeader.Trim('[', ']'), out _);

                if (!isAllowed)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                await next();
            });

            app.MapGet("/", () => Results.Content(GetMobileHtml(), "text/html; charset=utf-8"));

            app.MapPost("/auth", async (HttpContext ctx) =>
            {
                string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                DateTime now = DateTime.UtcNow;

                lock (_authAttemptsLock)
                {
                    if (_authAttempts.TryGetValue(clientIp, out var attempt))
                    {
                        if (attempt.until > now)
                        {
                            int retryAfterSec = Math.Max(1, (int)(attempt.until - now).TotalSeconds);
                            ctx.Response.Headers["Retry-After"] = retryAfterSec.ToString();
                            return Results.Json(new { ok = false, error = "Too many attempts" }, statusCode: StatusCodes.Status429TooManyRequests);
                        }
                    }
                }

                string? pin = null;
                if (ctx.Request.HasFormContentType)
                {
                    var form = await ctx.Request.ReadFormAsync();
                    pin = form["pin"].ToString();
                }
                else
                {
                    pin = ctx.Request.Query["pin"].ToString();
                }

                if (string.IsNullOrEmpty(pin) || pin.Trim() != _pin)
                {
                    await Task.Delay(500);
                    lock (_authAttemptsLock)
                    {
                        _authAttempts.TryGetValue(clientIp, out var attempt);
                        int newFails = attempt.fails + 1;
                        DateTime blockUntil = newFails >= 5 ? DateTime.UtcNow.AddMinutes(15) : DateTime.MinValue;
                        _authAttempts[clientIp] = (newFails, blockUntil);
                    }
                    return Results.Json(new { ok = false }, statusCode: 401);
                }

                lock (_authAttemptsLock)
                {
                    _authAttempts.Remove(clientIp);
                }

                string token = Guid.NewGuid().ToString("N");
                lock (_sessionsLock)
                {
                    _sessions.Add(token);
                }

                ctx.Response.Cookies.Append("edgedrop_session", token, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromHours(24),
                    Path = "/"
                });

                return Results.Json(new { ok = true });
            });

            app.MapGet("/auth-check", (HttpContext ctx) =>
            {
                return IsAuthorized(ctx) ? Results.Ok(new { ok = true }) : Results.Unauthorized();
            });

            app.MapPost("/upload", async (HttpRequest request) =>
            {
                if (!IsAuthorized(request.HttpContext))
                {
                    return Results.Unauthorized();
                }

                var maxBodyFeature = request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (maxBodyFeature != null && !maxBodyFeature.IsReadOnly)
                {
                    maxBodyFeature.MaxRequestBodySize = null;
                }

                string displayName = "File(s)";
                try
                {
                    if (!request.HasFormContentType || string.IsNullOrEmpty(request.ContentType))
                    {
                        return Results.BadRequest(new { error = "Multipart form-data expected" });
                    }

                    var mediaType = MediaTypeHeaderValue.Parse(request.ContentType);
                    var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
                    if (string.IsNullOrEmpty(boundary))
                    {
                        return Results.BadRequest(new { error = "Invalid multipart boundary header" });
                    }

                    var reader = new MultipartReader(boundary, request.Body);
                    var section = await reader.ReadNextSectionAsync();
                    if (section == null)
                    {
                        return Results.BadRequest(new { error = "No files selected" });
                    }

                    var appSettings = Settings.Load();
                    if (appSettings.MaxIncomingFileSizeMB > 0 && request.ContentLength.HasValue && request.ContentLength.Value > appSettings.MaxIncomingFileSizeMB * 1024L * 1024L)
                    {
                        return Results.Json(new { error = $"Exceeded file size limit ({appSettings.MaxIncomingFileSizeMB} MB)" }, statusCode: StatusCodes.Status413PayloadTooLarge);
                    }

                    long totalBytes = request.ContentLength ?? 0;

                    if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var initialCd) && initialCd.IsFileDisposition())
                    {
                        string headerFileName = initialCd.FileName.Value ?? initialCd.FileNameStar.Value ?? "";
                        if (!string.IsNullOrWhiteSpace(headerFileName))
                        {
                            displayName = Path.GetFileName(headerFileName);
                        }
                    }

                    string clientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Phone";
                    if (clientIp.StartsWith("::ffff:")) clientIp = clientIp.Substring(7);

                    bool isDangerous = FileReceiver.IsDangerousFile(displayName);
                    bool shouldPrompt = RequireAcceptIncoming || (SafeModeEnabled && isDangerous);

                    if (shouldPrompt && SenderApprovalManager.IsApproved(clientIp))
                    {
                        shouldPrompt = false;
                    }

                    if (shouldPrompt && AcceptFileRequested != null)
                    {
                        bool accepted = false;
                        try
                        {
                            using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                            var acceptTask = AcceptFileRequested.Invoke(displayName, clientIp, totalBytes);
                            var winner = await Task.WhenAny(acceptTask, Task.Delay(35000, acceptCts.Token));
                            if (winner == acceptTask)
                            {
                                accepted = await acceptTask;
                            }
                        }
                        catch
                        {
                            accepted = false;
                        }

                        if (!accepted)
                        {
                            SenderApprovalManager.Revoke(clientIp);
                            return Results.Json(new { error = "Transfer rejected on PC" }, statusCode: StatusCodes.Status403Forbidden);
                        }

                        SenderApprovalManager.Approve(clientIp);
                    }

                    var receivedNames = new List<string>();
                    Directory.CreateDirectory(_saveFolder);
                    CheckFreeDiskSpace(_saveFolder, request.ContentLength ?? (10L * 1024 * 1024));

                    while (section != null)
                    {
                        if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd) && cd.IsFileDisposition())
                        {
                            string rawFileName = cd.FileName.Value ?? cd.FileNameStar.Value ?? "uploaded_file";
                            string cleanFileName = Path.GetFileName(rawFileName);
                            if (!string.IsNullOrWhiteSpace(cleanFileName))
                            {
                                var (safePath, pathReservation) = FileNameHelper.ReserveUniquePath(_saveFolder, cleanFileName);
                                using (pathReservation)
                                {
                                    long fileExpectedSize = totalBytes > 0 ? totalBytes : (request.ContentLength ?? 0);
                                    UploadStarted?.Invoke(cleanFileName, fileExpectedSize);

                                    await using (var targetStream = new FileStream(safePath, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        var buf = new byte[81920];
                                        long totalReceived = 0;
                                        var sw = System.Diagnostics.Stopwatch.StartNew();
                                        long lastReportMs = 0;
                                        int readBytes;

                                        while ((readBytes = await section.Body.ReadAsync(buf.AsMemory(0, buf.Length))) > 0)
                                        {
                                            await targetStream.WriteAsync(buf.AsMemory(0, readBytes));
                                            totalReceived += readBytes;

                                            if (fileExpectedSize > 0)
                                            {
                                                long nowMs = sw.ElapsedMilliseconds;
                                                if (nowMs - lastReportMs >= 80 || totalReceived >= fileExpectedSize)
                                                {
                                                    lastReportMs = nowMs;
                                                    double ratio = Math.Clamp((double)totalReceived / fileExpectedSize, 0.0, 1.0);
                                                    double elapsedSec = sw.Elapsed.TotalSeconds;
                                                    double speed = elapsedSec > 0.05 ? totalReceived / elapsedSec : 0;
                                                    double remainingSec = speed > 0 && fileExpectedSize > totalReceived ? (fileExpectedSize - totalReceived) / speed : 0;
                                                    UploadProgress?.Invoke(cleanFileName, new TransferProgressReport(ratio, totalReceived, fileExpectedSize, speed, remainingSec));
                                                }
                                            }
                                        }
                                    }

                                    UploadCompleted?.Invoke(cleanFileName);
                                    receivedNames.Add(Path.GetFileName(safePath));
                                    Uploaded?.Invoke(safePath);
                                }
                            }
                        }

                        section = await reader.ReadNextSectionAsync();
                    }

                    return Results.Ok(new { received = receivedNames });
                }
                catch (Exception ex)
                {
                    UploadCancelled?.Invoke(displayName);
                    Error?.Invoke(ex.Message);
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            }).DisableAntiforgery();

            app.MapGet("/files", (HttpContext ctx, HttpResponse response) =>
            {
                if (!IsAuthorized(ctx))
                {
                    return Results.Unauthorized();
                }

                response.Headers.CacheControl = "no-store";
                var map = new Dictionary<string, (string name, long size, string modified, DateTime dt)>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in _offeredFiles)
                {
                    try
                    {
                        if (File.Exists(kvp.Value))
                        {
                            var fi = new FileInfo(kvp.Value);
                            map[fi.Name] = (fi.Name, fi.Length, fi.LastWriteTimeUtc.ToString("o"), fi.LastWriteTimeUtc);
                        }
                    }
                    catch { }
                }

                if (Directory.Exists(_saveFolder))
                {
                    try
                    {
                        var dir = new DirectoryInfo(_saveFolder);
                        var files = dir.GetFiles("*", SearchOption.TopDirectoryOnly);
                        foreach (var fi in files)
                        {
                            if (!map.ContainsKey(fi.Name))
                            {
                                map[fi.Name] = (fi.Name, fi.Length, fi.LastWriteTimeUtc.ToString("o"), fi.LastWriteTimeUtc);
                            }
                        }
                    }
                    catch { }
                }

                var list = map.Values
                    .OrderByDescending(x => x.dt)
                    .Select(x => (object)new { name = x.name, size = x.size, modified = x.modified })
                    .ToList();

                return Results.Ok(list);
            });

            app.MapGet("/download/{name}", (HttpContext ctx, string name) =>
            {
                if (!IsAuthorized(ctx))
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    return Results.NotFound();
                }

                string safeName = Path.GetFileName(name);
                if (string.IsNullOrWhiteSpace(safeName))
                {
                    return Results.NotFound();
                }

                if (_offeredFiles.TryGetValue(safeName, out var originalPath) && File.Exists(originalPath))
                {
                    return Results.File(originalPath, "application/octet-stream", safeName);
                }

                string canonicalFolder = Path.GetFullPath(_saveFolder);
                if (!canonicalFolder.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                    !canonicalFolder.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                {
                    canonicalFolder += Path.DirectorySeparatorChar;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(_saveFolder, name));
                }
                catch
                {
                    return Results.Forbid();
                }

                if (!fullPath.StartsWith(canonicalFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                if (File.Exists(fullPath))
                {
                    return Results.File(fullPath, "application/octet-stream", safeName);
                }

                return Results.NotFound();
            });

            app.MapGet("/events", async (HttpContext ctx, CancellationToken ct) =>
            {
                if (!IsAuthorized(ctx))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                string sessionId = ctx.Request.Query["sid"].ToString();
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = Guid.NewGuid().ToString("N");
                }

                string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                if (clientIp.StartsWith("::ffff:")) clientIp = clientIp.Substring(7);
                if (clientIp == "::1") clientIp = "127.0.0.1";

                string ua = ctx.Request.Headers.UserAgent.ToString();
                var (devName, isMobile) = ParseUserAgent(ua);
                var appSettings = Settings.Load();
                string resolvedName = appSettings.GetSavedDeviceName(clientIp, devName);

                var sessionInfo = new ActiveSessionInfo
                {
                    Id = sessionId,
                    Name = resolvedName,
                    OriginalDefaultName = devName,
                    Ip = clientIp,
                    Port = _port,
                    ConnectedAt = DateTime.UtcNow,
                    IsMobile = isMobile
                };

                _activeSessions[sessionId] = sessionInfo;
                ActiveSessionsChanged?.Invoke();

                ctx.Response.Headers.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.Connection = "keep-alive";

                var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
                void OnBroadcast(string json) => channel.Writer.TryWrite(json);

                lock (_subscribersLock)
                {
                    _sseSubscribers.Add(OnBroadcast);
                }

                try
                {
                    await ctx.Response.WriteAsync($": connected {sessionId}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);

                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, ctx.RequestAborted);
                    var linkedCt = linkedCts.Token;

                    while (!linkedCt.IsCancellationRequested)
                    {
                        var readTask = channel.Reader.ReadAsync(linkedCt).AsTask();
                        var timeoutTask = Task.Delay(2000, linkedCt);
                        var completedTask = await Task.WhenAny(readTask, timeoutTask);

                        if (completedTask == readTask)
                        {
                            var msg = await readTask;
                            await ctx.Response.WriteAsync($"data: {msg}\n\n", linkedCt);
                            await ctx.Response.Body.FlushAsync(linkedCt);
                        }
                        else
                        {
                            await ctx.Response.WriteAsync(": ping\n\n", linkedCt);
                            await ctx.Response.Body.FlushAsync(linkedCt);
                        }
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    lock (_subscribersLock)
                    {
                        _sseSubscribers.Remove(OnBroadcast);
                    }
                    if (_activeSessions.TryRemove(sessionId, out _))
                    {
                        ActiveSessionsChanged?.Invoke();
                    }
                }
            });

            app.MapPost("/session-end", (HttpRequest req) =>
            {
                string sid = req.Query["sid"].ToString();
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    if (_activeSessions.TryRemove(sid, out _))
                    {
                        ActiveSessionsChanged?.Invoke();
                    }
                }
                return Results.Ok();
            });

            app.MapGet("/latest-offer", (HttpContext ctx) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();
                return Results.Ok(_latestOffer);
            });

            app.MapPost("/dismiss-offer/{id}", (HttpContext ctx, string id) =>
            {
                if (!IsAuthorized(ctx)) return Results.Unauthorized();
                if (_latestOffer != null && _latestOffer.Id == id)
                {
                    _latestOffer = null;
                }
                return Results.Ok();
            });

            _app = app;
            await _app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _app = null;
            Error?.Invoke(ex.Message);
            throw;
        }
    }

    public void Start()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await StartAsync();
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
        });
    }

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        lock (_sessionsLock)
        {
            _sessions.Clear();
        }
        _activeSessions.Clear();
        try { ActiveSessionsChanged?.Invoke(); } catch { }

        if (app != null)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await app.StopAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
            finally
            {
                try
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    public void Stop()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync();
            }
            catch { }
        });
    }

    public void Dispose()
    {
        Stop();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static string? _cachedMobileHtml;

    public static string GetMobileHtml()
    {
        if (_cachedMobileHtml != null) return _cachedMobileHtml;

        try
        {
            string diskPath = Path.Combine(AppContext.BaseDirectory, "Web", "MobileHtml.html");
            if (File.Exists(diskPath))
            {
                _cachedMobileHtml = File.ReadAllText(diskPath);
                return _cachedMobileHtml;
            }

            var asm = typeof(WebServer).Assembly;
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("MobileHtml.html", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                    _cachedMobileHtml = reader.ReadToEnd();
                    return _cachedMobileHtml;
                }
            }
        }
        catch { }

        return "<!DOCTYPE html><html><body><h1>EdgeDrop Web</h1></body></html>";
    }

    private static bool IsLocalOrPrivateIp(System.Net.IPAddress ip)
    {
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            return false;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            byte[] b = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ((b[0] & 0xfe) == 0xfc);
        }

        return false;
    }

    private static void CheckFreeDiskSpace(string folder, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    long requiredWithMargin = requiredBytes + (10L * 1024 * 1024);
                    if (drive.AvailableFreeSpace < requiredWithMargin)
                    {
                        throw new IOException($"Insufficient free disk space ({drive.AvailableFreeSpace / 1024 / 1024} MB available, {requiredBytes / 1024 / 1024} MB required)");
                    }
                }
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch
        { }
    }
}
