using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AiContextBuilder.Core.Protocol;

namespace AiContextBuilder.Core.Comments;

public class CommentRemovalResult
{
    public string Code { get; set; } = string.Empty;
    public bool CommentsRemoved { get; set; }
    public bool Supported { get; set; }
}

public static class CommentHandler
{
    public static CommentRemovalResult RemoveComments(string source, string extension, LanguageName language = LanguageName.Auto)
    {
        string ext = extension.ToLowerInvariant();

        switch (ext)
        {
            case ".cs":
                return new CommentRemovalResult { Code = RemoveCSharpComments(source), CommentsRemoved = true, Supported = true };

            case ".vb":
                return new CommentRemovalResult { Code = RemoveVbComments(source), CommentsRemoved = true, Supported = true };

            case ".py":
                return new CommentRemovalResult { Code = RemovePythonComments(source), CommentsRemoved = true, Supported = true };

            case ".ts":
            case ".tsx":
            case ".js":
            case ".jsx":
            case ".mjs":
            case ".cjs":
            case ".mts":
            case ".cts":
            case ".java":
            case ".go":
                return new CommentRemovalResult { Code = RemoveCStyleComments(source, isGo: ext == ".go"), CommentsRemoved = true, Supported = true };

            case ".html":
            case ".htm":
                return new CommentRemovalResult { Code = RemoveHtmlComments(source), CommentsRemoved = true, Supported = true };

            case ".css":
                return new CommentRemovalResult { Code = RemoveCStyleComments(source, allowLineComments: false), CommentsRemoved = true, Supported = true };

            case ".scss":
            case ".sass":
                return new CommentRemovalResult { Code = RemoveCStyleComments(source, allowLineComments: true), CommentsRemoved = true, Supported = true };

            case ".razor":
            case ".cshtml":
                return new CommentRemovalResult { Code = RemoveRazorComments(source), CommentsRemoved = true, Supported = true };

            case ".vue":
                return new CommentRemovalResult { Code = RemoveVueComments(source), CommentsRemoved = true, Supported = true };

            case ".json":
            case ".yaml":
            case ".yml":
                if (ext == ".yaml" || ext == ".yml")
                {
                    return new CommentRemovalResult { Code = RemovePythonComments(source), CommentsRemoved = true, Supported = true };
                }
                return new CommentRemovalResult { Code = source, CommentsRemoved = false, Supported = true };

            default:
                return new CommentRemovalResult { Code = source, CommentsRemoved = false, Supported = false };
        }
    }

    public static string RemoveCSharpComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;
        int len = source.Length;

        while (i < len)
        {
            // Preprocessor directives (#region, #if, #pragma) at start of line
            if (source[i] == '#' && (i == 0 || source[i - 1] == '\n' || source.Substring(Math.Max(0, i - 10), Math.Min(i, 10)).Trim().Length == 0))
            {
                while (i < len && source[i] != '\n')
                {
                    sb.Append(source[i]);
                    i++;
                }
                continue;
            }

            // Verbatim string @"..." or $@""
            if ((source[i] == '@' && i + 1 < len && source[i + 1] == '"') ||
                (source[i] == '$' && i + 2 < len && source[i + 1] == '@' && source[i + 2] == '"'))
            {
                if (source[i] == '$')
                {
                    sb.Append("$@\"");
                    i += 3;
                }
                else
                {
                    sb.Append("@\"");
                    i += 2;
                }

                while (i < len)
                {
                    if (source[i] == '"' && i + 1 < len && source[i + 1] == '"')
                    {
                        sb.Append("\"\"");
                        i += 2;
                    }
                    else if (source[i] == '"')
                    {
                        sb.Append('"');
                        i++;
                        break;
                    }
                    else
                    {
                        sb.Append(source[i]);
                        i++;
                    }
                }
                continue;
            }

            // Raw string literal """ ... """
            if (i + 2 < len && source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"')
            {
                sb.Append("\"\"\"");
                i += 3;
                while (i + 2 < len && !(source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"'))
                {
                    sb.Append(source[i]);
                    i++;
                }
                if (i + 2 < len)
                {
                    sb.Append("\"\"\"");
                    i += 3;
                }
                continue;
            }

            // Standard string literals "..."
            if (source[i] == '"' || source[i] == '\'')
            {
                char quote = source[i];
                sb.Append(quote);
                i++;
                while (i < len)
                {
                    char c = source[i];
                    sb.Append(c);
                    if (c == '\\' && i + 1 < len)
                    {
                        i++;
                        sb.Append(source[i]);
                    }
                    else if (c == quote)
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Single line comment //
            if (source[i] == '/' && i + 1 < len && source[i + 1] == '/')
            {
                i += 2;
                while (i < len && source[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            // Multi line comment /* ... */
            if (source[i] == '/' && i + 1 < len && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < len && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }
                if (i + 1 < len) i += 2;
                continue;
            }

            sb.Append(source[i]);
            i++;
        }

        return sb.ToString();
    }

    public static string RemoveVbComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        bool inString = false;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }
            if (!inString && (c == '\'' || (i + 3 < source.Length && source.Substring(i, 4).Equals("REM ", StringComparison.OrdinalIgnoreCase))))
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }
                if (i < source.Length) sb.Append('\n');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Removes Python # comments while strictly preserving """ and ''' docstrings.
    /// </summary>
    public static string RemovePythonComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;
        int len = source.Length;

        while (i < len)
        {
            // Preserve shebang on line 1
            if (i == 0 && i + 1 < len && source[0] == '#' && source[1] == '!')
            {
                while (i < len && source[i] != '\n')
                {
                    sb.Append(source[i]);
                    i++;
                }
                continue;
            }

            // Triple quoted strings (docstrings)
            if (i + 2 < len && ((source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"') ||
                                (source[i] == '\'' && source[i + 1] == '\'' && source[i + 2] == '\'')))
            {
                string triple = source.Substring(i, 3);
                sb.Append(triple);
                i += 3;
                while (i + 2 < len && source.Substring(i, 3) != triple)
                {
                    if (source[i] == '\\' && i + 1 < len)
                    {
                        sb.Append(source[i]);
                        sb.Append(source[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        sb.Append(source[i]);
                        i++;
                    }
                }
                if (i + 2 < len)
                {
                    sb.Append(triple);
                    i += 3;
                }
                continue;
            }

            // Single line strings
            if (source[i] == '"' || source[i] == '\'')
            {
                char quote = source[i];
                sb.Append(quote);
                i++;
                while (i < len)
                {
                    char c = source[i];
                    sb.Append(c);
                    if (c == '\\' && i + 1 < len)
                    {
                        i++;
                        sb.Append(source[i]);
                    }
                    else if (c == quote)
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Python comment #
            if (source[i] == '#')
            {
                while (i < len && source[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            sb.Append(source[i]);
            i++;
        }

        return sb.ToString();
    }

    public static string RemoveCStyleComments(string source, bool allowLineComments = true, bool isGo = false)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;
        int len = source.Length;

        while (i < len)
        {
            // Go raw string literal `...`
            if (isGo && source[i] == '`')
            {
                sb.Append('`');
                i++;
                while (i < len && source[i] != '`')
                {
                    sb.Append(source[i]);
                    i++;
                }
                if (i < len)
                {
                    sb.Append('`');
                    i++;
                }
                continue;
            }

            // String literals
            if (source[i] == '"' || source[i] == '\'' || source[i] == '`')
            {
                char quote = source[i];
                sb.Append(quote);
                i++;
                while (i < len)
                {
                    char c = source[i];
                    sb.Append(c);
                    if (c == '\\' && i + 1 < len)
                    {
                        i++;
                        sb.Append(source[i]);
                    }
                    else if (c == quote)
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Line comment //
            if (allowLineComments && source[i] == '/' && i + 1 < len && source[i + 1] == '/')
            {
                i += 2;
                while (i < len && source[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            // Block comment /* ... */
            if (source[i] == '/' && i + 1 < len && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < len && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }
                if (i + 1 < len) i += 2;
                continue;
            }

            sb.Append(source[i]);
            i++;
        }

        return sb.ToString();
    }

    public static string RemoveHtmlComments(string source) =>
        Regex.Replace(source, @"<!--[\s\S]*?-->", string.Empty);

    public static string RemoveRazorComments(string source)
    {
        string withoutRazor = Regex.Replace(source, @"@\*[\s\S]*?\*@", string.Empty);
        return RemoveHtmlComments(withoutRazor);
    }

    public static string RemoveVueComments(string source)
    {
        string s = Regex.Replace(source, @"(<template[\s\S]*?>)([\s\S]*?)(</template>)", m =>
            m.Groups[1].Value + RemoveHtmlComments(m.Groups[2].Value) + m.Groups[3].Value, RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"(<script[\s\S]*?>)([\s\S]*?)(</script>)", m =>
            m.Groups[1].Value + RemoveCStyleComments(m.Groups[2].Value) + m.Groups[3].Value, RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"(<style[\s\S]*?>)([\s\S]*?)(</style>)", m =>
            m.Groups[1].Value + RemoveCStyleComments(m.Groups[2].Value, allowLineComments: true) + m.Groups[3].Value, RegexOptions.IgnoreCase);

        return s;
    }
}
