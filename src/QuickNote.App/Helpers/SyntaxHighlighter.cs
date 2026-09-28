using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickNote.App.Helpers
{
    public static class SyntaxHighlighter
    {
        private static readonly SolidColorBrush BrushDefault = new SolidColorBrush(Color.FromRgb(228, 228, 231));   // #E4E4E7
        private static readonly SolidColorBrush BrushKeyword = new SolidColorBrush(Color.FromRgb(96, 165, 250));    // #60A5FA Blue
        private static readonly SolidColorBrush BrushBuiltin = new SolidColorBrush(Color.FromRgb(192, 132, 252));   // #C084FC Purple
        private static readonly SolidColorBrush BrushString = new SolidColorBrush(Color.FromRgb(52, 211, 153));    // #34D399 Emerald Green
        private static readonly SolidColorBrush BrushNumber = new SolidColorBrush(Color.FromRgb(251, 191, 36));    // #FBBF24 Amber
        private static readonly SolidColorBrush BrushComment = new SolidColorBrush(Color.FromRgb(113, 113, 122));  // #71717A Gray
        private static readonly SolidColorBrush BrushVariable = new SolidColorBrush(Color.FromRgb(56, 189, 248));  // #38BDF8 Sky Blue
        private static readonly SolidColorBrush BrushParam = new SolidColorBrush(Color.FromRgb(244, 114, 182));    // #F472B6 Pink

        static SyntaxHighlighter()
        {
            BrushDefault.Freeze();
            BrushKeyword.Freeze();
            BrushBuiltin.Freeze();
            BrushString.Freeze();
            BrushNumber.Freeze();
            BrushComment.Freeze();
            BrushVariable.Freeze();
            BrushParam.Freeze();
        }

        public static string NormalizeLanguage(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "text";
            var clean = lang.Trim().ToLowerInvariant();
            return clean switch
            {
                "pwsh" or "ps" or "ps1" => "powershell",
                "cs" or "dotnet" or "c#" => "csharp",
                "py" => "python",
                "js" or "javascript" or "mjs" or "cjs" => "javascript",
                "ts" or "typescript" => "typescript",
                "sh" or "shell" or "zsh" or "bash" => "bash",
                "cmd" or "bat" => "batch",
                "yml" => "yaml",
                "htm" => "html",
                "c++" => "cpp",
                "plaintext" or "plain" or "txt" => "text",
                "docker" => "dockerfile",
                _ => clean
            };
        }

        public static List<Inline> Highlight(string code, string language)
        {
            var inlines = new List<Inline>();
            var normalizedLang = NormalizeLanguage(language);

            if (string.IsNullOrEmpty(code))
            {
                inlines.Add(new Run(string.Empty) { Foreground = BrushDefault });
                return inlines;
            }

            var lines = code.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    inlines.Add(new LineBreak());
                }

                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                HighlightLine(line, normalizedLang, inlines);
            }

            return inlines;
        }

        private static void HighlightLine(string line, string lang, List<Inline> inlines)
        {
            switch (lang)
            {
                case "powershell":
                    HighlightPowerShell(line, inlines);
                    break;
                case "csharp":
                case "cpp":
                case "c":
                case "java":
                case "go":
                case "rust":
                case "kotlin":
                case "swift":
                case "dart":
                    HighlightCSharp(line, inlines);
                    break;
                case "python":
                case "ruby":
                    HighlightPython(line, inlines);
                    break;
                case "javascript":
                case "typescript":
                case "php":
                    HighlightJavaScript(line, inlines);
                    break;
                case "sql":
                    HighlightSQL(line, inlines);
                    break;
                case "json":
                    HighlightJson(line, inlines);
                    break;
                case "bash":
                case "batch":
                case "dockerfile":
                    HighlightBash(line, inlines);
                    break;
                case "html":
                case "xml":
                    HighlightXml(line, inlines);
                    break;
                case "yaml":
                    HighlightYaml(line, inlines);
                    break;
                default:
                    HighlightGeneric(line, inlines);
                    break;
            }
        }

        private static void HighlightPowerShell(string line, List<Inline> inlines)
        {
            // Token regex pattern: comments, strings, variables, parameters, numbers, words, others
            var pattern = @"(?<comment>#.*$)|(?<str>""[^""]*""|'[^']*')|(?<var>\$[\w:]+)|(?<param>-[\w]+)|(?<num>\b\d+(\.\d+)?\b)|(?<word>\b[a-zA-Z_-][\w-]*\b)|(?<other>[^\w\s""'#$-]+|\s+)";
            var matches = Regex.Matches(line, pattern);

            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "function", "param", "if", "else", "elseif", "foreach", "for", "while", "switch",
                "return", "try", "catch", "finally", "throw", "break", "continue", "in", "trap",
                "process", "begin", "end"
            };

            var builtins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ping", "Get-Process", "Stop-Process", "Start-Process", "Get-Item", "Set-Item",
                "Copy-Item", "Remove-Item", "New-Item", "Get-Content", "Set-Content", "Add-Content",
                "Write-Host", "Write-Output", "Write-Error", "Select-Object", "Where-Object",
                "Invoke-Expression", "Invoke-WebRequest", "Invoke-RestMethod", "Format-List",
                "Format-Table", "Out-File", "Test-Path", "cd", "dir", "ls", "echo", "cls", "cat"
            };

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["var"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushVariable });
                }
                else if (m.Groups["param"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushParam });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else if (m.Groups["word"].Success)
                {
                    var word = m.Value;
                    if (keywords.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushKeyword });
                    }
                    else if (builtins.Contains(word) || word.Contains("-"))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushBuiltin });
                    }
                    else
                    {
                        inlines.Add(new Run(word) { Foreground = BrushDefault });
                    }
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightCSharp(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment>//.*$)|(?<str>""[^""]*""|'[^']*'|\$""[^""]*"")|(?<num>\b\d+(\.\d+)?\b)|(?<word>\b[a-zA-Z_]\w*\b)|(?<other>[^\w\s""'/]+|\s+|/)";
            var matches = Regex.Matches(line, pattern);

            var keywords = new HashSet<string>
            {
                "using", "namespace", "class", "struct", "interface", "record", "enum", "public",
                "private", "protected", "internal", "static", "readonly", "const", "async", "await",
                "new", "return", "if", "else", "for", "foreach", "while", "switch", "case", "break",
                "continue", "try", "catch", "finally", "throw", "var", "int", "string", "bool",
                "double", "float", "void", "object", "null", "true", "false", "get", "set", "init",
                "override", "virtual", "this", "base"
            };

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else if (m.Groups["word"].Success)
                {
                    var word = m.Value;
                    if (keywords.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushKeyword });
                    }
                    else if (char.IsUpper(word[0]))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushBuiltin });
                    }
                    else
                    {
                        inlines.Add(new Run(word) { Foreground = BrushDefault });
                    }
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightPython(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment>#.*$)|(?<str>""""""[\s\S]*?""""""|'''.*?'''|""[^""]*""|'[^']*'|f""[^""]*"")|(?<decorator>@\w+)|(?<num>\b\d+(\.\d+)?\b)|(?<word>\b[a-zA-Z_]\w*\b)|(?<other>[^\w\s""'#@]+|\s+)";
            var matches = Regex.Matches(line, pattern);

            var keywords = new HashSet<string>
            {
                "def", "class", "import", "from", "as", "return", "if", "elif", "else", "for",
                "while", "try", "except", "finally", "with", "yield", "lambda", "pass", "break",
                "continue", "raise", "in", "is", "not", "and", "or", "True", "False", "None", "async", "await"
            };

            var builtins = new HashSet<string>
            {
                "print", "len", "range", "open", "str", "int", "float", "list", "dict", "set",
                "tuple", "type", "isinstance", "enumerate", "zip", "map", "filter", "self"
            };

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["decorator"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushParam });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else if (m.Groups["word"].Success)
                {
                    var word = m.Value;
                    if (keywords.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushKeyword });
                    }
                    else if (builtins.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushBuiltin });
                    }
                    else
                    {
                        inlines.Add(new Run(word) { Foreground = BrushDefault });
                    }
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightJavaScript(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment>//.*$)|(?<str>`[^`]*`|""[^""]*""|'[^']*')|(?<num>\b\d+(\.\d+)?\b)|(?<word>\b[a-zA-Z_$][\w$]*\b)|(?<other>[^\w\s""'`$/]+|\s+|/)";
            var matches = Regex.Matches(line, pattern);

            var keywords = new HashSet<string>
            {
                "function", "const", "let", "var", "return", "if", "else", "for", "while",
                "switch", "case", "break", "continue", "try", "catch", "finally", "throw", "new",
                "class", "extends", "import", "export", "from", "default", "async", "await",
                "typeof", "instanceof", "true", "false", "null", "undefined", "this"
            };

            var builtins = new HashSet<string>
            {
                "console", "document", "window", "Math", "JSON", "Promise", "Array", "Object",
                "String", "Number", "Boolean", "Date", "RegExp", "Map", "Set", "log", "error", "warn"
            };

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else if (m.Groups["word"].Success)
                {
                    var word = m.Value;
                    if (keywords.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushKeyword });
                    }
                    else if (builtins.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushBuiltin });
                    }
                    else
                    {
                        inlines.Add(new Run(word) { Foreground = BrushDefault });
                    }
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightSQL(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment>--.*$)|(?<str>'[^']*')|(?<num>\b\d+(\.\d+)?\b)|(?<word>\b[a-zA-Z_]\w*\b)|(?<other>[^\w\s'-]+|\s+|-)";
            var matches = Regex.Matches(line, pattern);

            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SELECT", "FROM", "WHERE", "INSERT", "INTO", "UPDATE", "DELETE", "CREATE", "TABLE",
                "DROP", "ALTER", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON",
                "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET", "AND", "OR", "NOT", "IN",
                "LIKE", "IS", "NULL", "AS", "DISTINCT", "UNION", "ALL", "CASE", "WHEN", "THEN",
                "ELSE", "END", "EXISTS", "BETWEEN", "ASC", "DESC", "VALUES", "SET", "PRIMARY", "KEY"
            };

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else if (m.Groups["word"].Success)
                {
                    var word = m.Value;
                    if (keywords.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushKeyword });
                    }
                    else
                    {
                        inlines.Add(new Run(word) { Foreground = BrushDefault });
                    }
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightJson(string line, List<Inline> inlines)
        {
            var pattern = @"(?<key>""[^""]*""\s*:)|(?<str>""[^""]*"")|(?<num>\b\d+(\.\d+)?\b)|(?<bool>\b(true|false|null)\b)|(?<other>[^\w\s""]+|\s+)";
            var matches = Regex.Matches(line, pattern);

            foreach (Match m in matches)
            {
                if (m.Groups["key"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushVariable });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else if (m.Groups["bool"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushKeyword });
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightBash(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment>#.*$)|(?<str>""[^""]*""|'[^']*')|(?<var>\$[\w?@#*!]+|\$\{[^}]+\})|(?<param>-{1,2}[\w-]+)|(?<num>\b\d+\b)|(?<word>\b[a-zA-Z_][\w-]*\b)|(?<other>[^\w\s""'#$-]+|\s+)";
            var matches = Regex.Matches(line, pattern);

            var keywords = new HashSet<string>
            {
                "if", "then", "else", "elif", "fi", "for", "while", "do", "done", "case", "esac",
                "function", "return", "exit", "sudo", "export", "source", "set"
            };

            var builtins = new HashSet<string>
            {
                "echo", "cd", "ls", "cat", "grep", "awk", "sed", "curl", "wget", "chmod", "chown",
                "mkdir", "rm", "cp", "mv", "tar", "ssh", "git", "ping", "which", "find"
            };

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["var"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushVariable });
                }
                else if (m.Groups["param"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushParam });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else if (m.Groups["word"].Success)
                {
                    var word = m.Value;
                    if (keywords.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushKeyword });
                    }
                    else if (builtins.Contains(word))
                    {
                        inlines.Add(new Run(word) { Foreground = BrushBuiltin });
                    }
                    else
                    {
                        inlines.Add(new Run(word) { Foreground = BrushDefault });
                    }
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightGeneric(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment>//.*$|#.*$)|(?<str>""[^""]*""|'[^']*')|(?<num>\b\d+(\.\d+)?\b)|(?<word>\b[a-zA-Z_]\w*\b)|(?<other>[^\w\s""'/]+|\s+|/)";
            var matches = Regex.Matches(line, pattern);

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightXml(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment><!--.*?-->)|(?<tag></?[a-zA-Z0-9_:-]+)|(?<attr>[a-zA-Z0-9_:-]+(?=\=))|(?<str>""[^""]*""|'[^']*')|(?<other>[^<""'=-]+|<|/|>|=)";
            var matches = Regex.Matches(line, pattern);

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["tag"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushKeyword });
                }
                else if (m.Groups["attr"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushParam });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }

        private static void HighlightYaml(string line, List<Inline> inlines)
        {
            var pattern = @"(?<comment>#.*$)|(?<key>^\s*[\w.-]+(?=\s*:))|(?<str>""[^""]*""|'[^']*')|(?<num>\b\d+(\.\d+)?\b)|(?<other>[^\w\s""'#:-]+|:|\s+)";
            var matches = Regex.Matches(line, pattern);

            foreach (Match m in matches)
            {
                if (m.Groups["comment"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushComment });
                }
                else if (m.Groups["key"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushKeyword });
                }
                else if (m.Groups["str"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushString });
                }
                else if (m.Groups["num"].Success)
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushNumber });
                }
                else
                {
                    inlines.Add(new Run(m.Value) { Foreground = BrushDefault });
                }
            }
        }
    }
}
