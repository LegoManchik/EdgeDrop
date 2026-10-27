using System;
using System.Collections.Generic;
using QRCoder;

namespace EdgeDrop.Cli;

public static class QrRenderer
{
    public static void Print(string payload, string? caption = null, string? pin = null)
    {
        string qrPayload = payload;
        if (!string.IsNullOrEmpty(pin) && !payload.Contains("pin=", StringComparison.OrdinalIgnoreCase))
        {
            qrPayload = payload.Contains('?') ? $"{payload}&pin={pin}" : $"{payload.TrimEnd('/')}/?pin={pin}";
        }

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new { qr_url = qrPayload, pin, caption });
            return;
        }

        try
        {
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.M);
            var matrix = qrCodeData.ModuleMatrix;

            int height = matrix.Count;
            int width = matrix[0].Count;

            Console.WriteLine();
            if (!string.IsNullOrEmpty(caption))
            {
                Console.WriteLine($"  {ConsoleRenderer.BrandLight}{ConsoleRenderer.Bold}{caption}{ConsoleRenderer.Reset}");
            }
            Console.WriteLine($"  {ConsoleRenderer.MutedColor}URL:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{payload}{ConsoleRenderer.Reset}");
            if (!string.IsNullOrEmpty(pin))
            {
                Console.WriteLine($"  {ConsoleRenderer.MutedColor}PIN:{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{ConsoleRenderer.Bold}{pin}{ConsoleRenderer.Reset}");
            }
            Console.WriteLine();

            const string WhiteBgBlackFg = "\x1b[47m\x1b[30m";
            const string Reset = "\x1b[0m";

            int quietBorder = 2;
            int totalCols = width + (quietBorder * 2);

            Console.Write("    " + WhiteBgBlackFg);
            Console.Write(new string(' ', totalCols));
            Console.WriteLine(Reset);

            for (int y = 0; y < height; y += 2)
            {
                Console.Write("    " + WhiteBgBlackFg);
                Console.Write(new string(' ', quietBorder));

                for (int x = 0; x < width; x++)
                {
                    bool topDark = matrix[y][x];
                    bool bottomDark = (y + 1 < height) && matrix[y + 1][x];

                    if (topDark && bottomDark)
                    {
                        Console.Write('█');
                    }
                    else if (topDark && !bottomDark)
                    {
                        Console.Write('▀');
                    }
                    else if (!topDark && bottomDark)
                    {
                        Console.Write('▄');
                    }
                    else
                    {
                        Console.Write(' ');
                    }
                }

                Console.Write(new string(' ', quietBorder));
                Console.WriteLine(Reset);
            }

            Console.Write("    " + WhiteBgBlackFg);
            Console.Write(new string(' ', totalCols));
            Console.WriteLine(Reset);

            Console.WriteLine();
            if (!string.IsNullOrEmpty(pin))
            {
                Console.WriteLine($"  {ConsoleRenderer.BrandLight}{ConsoleRenderer.Bold}PIN CODE:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{ConsoleRenderer.Bold}{pin}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}(enter on phone if prompted){ConsoleRenderer.Reset}");
                Console.WriteLine($"  {ConsoleRenderer.DarkMuted}Scan with your phone camera to connect and log in automatically.{ConsoleRenderer.Reset}");
            }
            else
            {
                Console.WriteLine($"  {ConsoleRenderer.DarkMuted}Scan with your phone camera to connect instantly.{ConsoleRenderer.Reset}");
            }
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Error($"Failed to generate QR code: {ex.Message}");
        }
    }
}
