using System;
using System.IO;
using System.Linq;
using EdgeDrop.Services;

namespace EdgeDrop.Cli.Commands;

public static class HistoryCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase) || args[0].Equals("clean", StringComparison.OrdinalIgnoreCase)))
        {
            HistoryService.Clear();
            if (ConsoleRenderer.JsonMode)
            {
                ConsoleRenderer.WriteJson(new { status = "cleared" });
            }
            else
            {
                ConsoleRenderer.Success("Transfer history cleared.");
            }
            return 0;
        }

        int count = 20;
        for (int i = 0; i < args.Length; i++)
        {
            if (int.TryParse(args[i], out int c) && c > 0)
            {
                count = Math.Clamp(c, 1, 200);
            }
        }

        var items = HistoryService.Load(count);

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(items);
            return 0;
        }

        if (items.Count == 0)
        {
            ConsoleRenderer.Info("Transfer history is empty.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.DarkMuted}{"TIME",-20} {"DIR",-6} {"STATUS",-16} {"FILE",-30} {"PATH"}{ConsoleRenderer.Reset}");

        foreach (var item in items)
        {
            string dirIcon = item.IsIncoming ? "↓ IN " : "↑ OUT";
            string dirColor = item.IsIncoming ? ConsoleRenderer.BrandLight : ConsoleRenderer.SuccessColor;

            string statusColor = item.IsError ? ConsoleRenderer.ErrorColor : ConsoleRenderer.TextLight;
            string status = item.StatusText ?? "";
            if (status.Length > 16) status = status[..13] + "...";

            string fileName = Path.GetFileName(item.FileName);
            if (fileName.Length > 28) fileName = fileName[..25] + "...";

            string filePath = item.FilePath ?? "";
            if (filePath.Length > 35) filePath = "..." + filePath[^32..];

            Console.WriteLine($"  {ConsoleRenderer.DarkMuted}{item.Timestamp,-20}{ConsoleRenderer.Reset} {dirColor}{dirIcon,-6}{ConsoleRenderer.Reset} {statusColor}{status,-16}{ConsoleRenderer.Reset} {ConsoleRenderer.Bold}{fileName,-30}{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}{filePath}{ConsoleRenderer.Reset}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.DarkMuted}Showing {items.Count} entries. Use 'history clear' to reset.{ConsoleRenderer.Reset}");
        return 0;
    }
}
