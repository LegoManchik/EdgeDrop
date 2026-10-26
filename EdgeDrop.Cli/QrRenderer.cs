using System;
using System.Collections.Generic;
using QRCoder;

namespace EdgeDrop.Cli;

public static class QrRenderer
{
    /// <summary>
    /// Renders a sharp, compact QR code using Unicode half-block characters
    /// (2 vertical modules per terminal character cell).
    /// </summary>
    public static void Print(string payload, string? caption = null)
    {
        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new { qr_url = payload, caption });
            return;
        }

        try
        {
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var matrix = qrCodeData.ModuleMatrix;

            int height = matrix.Count;
            int width = matrix[0].Count;

            Console.WriteLine();
            if (!string.IsNullOrEmpty(caption))
            {
                Console.WriteLine($"  {ConsoleRenderer.BrandLight}{ConsoleRenderer.Bold}{caption}{ConsoleRenderer.Reset}");
            }
            Console.WriteLine($"  {ConsoleRenderer.MutedColor}URL:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{payload}{ConsoleRenderer.Reset}");
            Console.WriteLine();

            // Colors: White background (\x1b[47m), Black foreground (\x1b[30m)
            // ▀ (\u2580): top half is foreground (black), bottom is background (white)
            // ▄ (\u2584): top is background (white), bottom is foreground (black)
            // █ (\u2588): both top and bottom are black
            // ' ': both top and bottom are white

            const string WhiteBgBlackFg = "\x1b[47m\x1b[30m";
            const string Reset = "\x1b[0m";

            int quietBorder = 2; // modules of quiet zone
            int totalCols = width + (quietBorder * 2);

            // Top quiet border (1 character row = 2 module rows of pure white)
            Console.Write("    " + WhiteBgBlackFg);
            Console.Write(new string(' ', totalCols));
            Console.WriteLine(Reset);

            // Render matrix 2 module rows at a time
            for (int y = 0; y < height; y += 2)
            {
                Console.Write("    " + WhiteBgBlackFg);

                // Left quiet zone
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

                // Right quiet zone
                Console.Write(new string(' ', quietBorder));
                Console.WriteLine(Reset);
            }

            // Bottom quiet border
            Console.Write("    " + WhiteBgBlackFg);
            Console.Write(new string(' ', totalCols));
            Console.WriteLine(Reset);

            Console.WriteLine();
            Console.WriteLine($"  {ConsoleRenderer.DarkMuted}Scan with your phone camera to connect instantly.{ConsoleRenderer.Reset}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Error($"Failed to generate QR code: {ex.Message}");
        }
    }
}
