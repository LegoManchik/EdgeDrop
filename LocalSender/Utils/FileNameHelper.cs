using System;
using System.Collections.Generic;
using System.IO;

namespace EdgeDrop;

public static class FileNameHelper
{
    private static readonly object _pathLock = new();
    private static readonly HashSet<string> _reservedPaths = new(StringComparer.OrdinalIgnoreCase);

    public static string MakeUniquePath(string folder, string fileName)
    {
        lock (_pathLock)
        {
            var (path, reservation) = ReserveUniquePathInternal(folder, fileName);
            reservation.Dispose();
            return path;
        }
    }

    public static (string TargetPath, IDisposable Reservation) ReserveUniquePath(string folder, string fileName)
    {
        lock (_pathLock)
        {
            return ReserveUniquePathInternal(folder, fileName);
        }
    }

    private static (string TargetPath, IDisposable Reservation) ReserveUniquePathInternal(string folder, string fileName)
    {
        fileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "uploaded_file";
        }

        string path = Path.Combine(folder, fileName);
        string partPath = path + ".edpart";
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int i = 1;

        while (File.Exists(path) || File.Exists(partPath) || _reservedPaths.Contains(path) || _reservedPaths.Contains(partPath))
        {
            path = Path.Combine(folder, $"{baseName} ({i}){ext}");
            partPath = path + ".edpart";
            i++;
        }

        _reservedPaths.Add(path);
        _reservedPaths.Add(partPath);

        return (path, new PathReservation(path, partPath));
    }

    private sealed class PathReservation : IDisposable
    {
        private readonly string _path;
        private readonly string _partPath;
        private bool _disposed;

        public PathReservation(string path, string partPath)
        {
            _path = path;
            _partPath = partPath;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_pathLock)
            {
                _reservedPaths.Remove(_path);
                _reservedPaths.Remove(_partPath);
            }
        }
    }
}
