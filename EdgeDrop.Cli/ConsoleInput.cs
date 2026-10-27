using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EdgeDrop.Cli;

public static class ConsoleInput
{
    private static readonly string[] Commands = new[]
    {
        "send", "receive", "devices", "scan", "web", "qr", "history", "config", "open", "ping", "status", "help", "clear", "exit"
    };

    public static string? ReadLine(string prompt, List<string> history)
    {
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine();
        }

        var buffer = new StringBuilder();
        int cursorPos = 0;
        int historyIndex = history.Count;
        string savedDraft = "";

        List<string>? tabMatches = null;
        int tabIndex = -1;
        int tabWordStart = 0;
        int tabWordLength = 0;

        int promptLength = GetVisualLength(prompt);

        Console.Write(prompt);

        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);

            if (keyInfo.Key != ConsoleKey.Tab)
            {
                tabMatches = null;
                tabIndex = -1;
            }

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                string result = buffer.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                {
                    if (history.Count == 0 || !history[^1].Equals(result, StringComparison.OrdinalIgnoreCase))
                    {
                        history.Add(result);
                    }
                }
                return result;
            }

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                buffer.Clear();
                cursorPos = 0;
                RedrawLine(prompt, buffer, cursorPos, promptLength);
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (cursorPos > 0)
                {
                    cursorPos--;
                    buffer.Remove(cursorPos, 1);
                    RedrawLine(prompt, buffer, cursorPos, promptLength);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Delete)
            {
                if (cursorPos < buffer.Length)
                {
                    buffer.Remove(cursorPos, 1);
                    RedrawLine(prompt, buffer, cursorPos, promptLength);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.LeftArrow)
            {
                if (cursorPos > 0)
                {
                    cursorPos--;
                    SetCursor(promptLength + cursorPos);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.RightArrow)
            {
                if (cursorPos < buffer.Length)
                {
                    cursorPos++;
                    SetCursor(promptLength + cursorPos);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Home)
            {
                cursorPos = 0;
                SetCursor(promptLength);
                continue;
            }

            if (keyInfo.Key == ConsoleKey.End)
            {
                cursorPos = buffer.Length;
                SetCursor(promptLength + cursorPos);
                continue;
            }

            if (keyInfo.Key == ConsoleKey.UpArrow)
            {
                if (history.Count == 0) continue;

                if (historyIndex == history.Count)
                {
                    savedDraft = buffer.ToString();
                }

                if (historyIndex > 0)
                {
                    historyIndex--;
                    buffer.Clear();
                    buffer.Append(history[historyIndex]);
                    cursorPos = buffer.Length;
                    RedrawLine(prompt, buffer, cursorPos, promptLength);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.DownArrow)
            {
                if (historyIndex < history.Count - 1)
                {
                    historyIndex++;
                    buffer.Clear();
                    buffer.Append(history[historyIndex]);
                    cursorPos = buffer.Length;
                    RedrawLine(prompt, buffer, cursorPos, promptLength);
                }
                else if (historyIndex == history.Count - 1)
                {
                    historyIndex = history.Count;
                    buffer.Clear();
                    buffer.Append(savedDraft);
                    cursorPos = buffer.Length;
                    RedrawLine(prompt, buffer, cursorPos, promptLength);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Tab)
            {
                HandleTabCompletion(buffer, ref cursorPos, ref tabMatches, ref tabIndex, ref tabWordStart, ref tabWordLength);
                RedrawLine(prompt, buffer, cursorPos, promptLength);
                continue;
            }

            if (!char.IsControl(keyInfo.KeyChar))
            {
                buffer.Insert(cursorPos, keyInfo.KeyChar);
                cursorPos++;
                RedrawLine(prompt, buffer, cursorPos, promptLength);
            }
        }
    }

    private static void HandleTabCompletion(
        StringBuilder buffer,
        ref int cursorPos,
        ref List<string>? matches,
        ref int tabIndex,
        ref int wordStart,
        ref int wordLength)
    {
        string fullText = buffer.ToString();

        if (matches == null)
        {
            wordStart = cursorPos;
            while (wordStart > 0 && !char.IsWhiteSpace(fullText[wordStart - 1]))
            {
                wordStart--;
            }

            wordLength = cursorPos - wordStart;
            string prefix = fullText.Substring(wordStart, wordLength).Trim('"');

            bool isFirstWord = !fullText.Substring(0, wordStart).Any(c => !char.IsWhiteSpace(c));

            if (isFirstWord)
            {
                matches = Commands
                    .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                matches = GetFileSystemMatches(prefix);
            }

            if (matches == null || matches.Count == 0)
            {
                return;
            }

            tabIndex = 0;
        }
        else
        {
            tabIndex = (tabIndex + 1) % matches.Count;
        }

        string replacement = matches[tabIndex];
        if (replacement.Contains(' ') && !replacement.StartsWith("\""))
        {
            replacement = $"\"{replacement}\"";
        }

        buffer.Remove(wordStart, wordLength);
        buffer.Insert(wordStart, replacement);
        wordLength = replacement.Length;
        cursorPos = wordStart + wordLength;
    }

    private static List<string> GetFileSystemMatches(string prefix)
    {
        var list = new List<string>();
        try
        {
            string dir = ".";
            string searchPattern = "*";

            if (prefix.Contains('/') || prefix.Contains('\\'))
            {
                dir = Path.GetDirectoryName(prefix) ?? ".";
                if (string.IsNullOrEmpty(dir)) dir = ".";
                searchPattern = Path.GetFileName(prefix) + "*";
            }
            else if (!string.IsNullOrEmpty(prefix))
            {
                searchPattern = prefix + "*";
            }

            if (Directory.Exists(dir))
            {
                foreach (var d in Directory.GetDirectories(dir, searchPattern))
                {
                    list.Add(d + Path.DirectorySeparatorChar);
                }

                foreach (var f in Directory.GetFiles(dir, searchPattern))
                {
                    list.Add(f);
                }
            }
        }
        catch { }

        return list;
    }

    private static void RedrawLine(string prompt, StringBuilder buffer, int cursorPos, int promptLength)
    {
        try
        {
            Console.Write($"\r\x1b[2K{prompt}{buffer}");
            SetCursor(promptLength + cursorPos);
        }
        catch { }
    }

    private static void SetCursor(int col)
    {
        try
        {
            Console.Write($"\r\x1b[{col + 1}G");
        }
        catch { }
    }

    private static int GetVisualLength(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        string clean = Regex.Replace(text, @"\x1b\[[0-9;]*[a-zA-Z]", "");
        return clean.Length;
    }
}
