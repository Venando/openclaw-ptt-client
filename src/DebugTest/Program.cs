using System.Text;

class Program
{
    static void Main()
    {
        var spectreMarkup = "This [bold yellow]sentence[/] has [bold]everything[/]: [italic]italics[/] [strikethrough]strikethrough[/] [bold yellow]code[/] [link=https://openclaw.ai]links[/] [bold italic]bold italic[/] more [bold yellow][[code]][/] with [bold yellow][[brackets]][/] and [bold yellow][[\"arrays\"]][/] and[bold]Check[/]";
        
        // Direct simulation of ProcessMarkupDelta's core logic
        bool insideTag = false;
        var openMarkupTags = new Stack<string>();
        var wordBuffer = new StringBuilder();
        int visibleWordLen = 0;
        int currentLineLength = 0;
        int availableWidth = 65; // 70 - 5 margin
        
        for (int i = 0; i < spectreMarkup.Length; i++)
        {
            char c = spectreMarkup[i];
            
            if (!insideTag && c == '[')
            {
                if (i + 1 < spectreMarkup.Length && spectreMarkup[i + 1] == '[')
                {
                    wordBuffer.Append(c);
                    wordBuffer.Append(c);
                    i++;
                    visibleWordLen++;
                    continue;
                }
                if (i > 0 && !(i + 1 < spectreMarkup.Length && spectreMarkup[i + 1] == '/'))
                {
                    char prev = spectreMarkup[i - 1];
                    if (char.IsLetterOrDigit(prev) || prev == ')' || prev == '>' || prev == '}' || prev == '"' || prev == '\'')
                    {
                        wordBuffer.Append(c);
                        visibleWordLen++;
                        continue;
                    }
                }
                insideTag = true;
                wordBuffer.Append(c);
                continue;
            }
            
            if (insideTag && c == ']')
            {
                insideTag = false;
                wordBuffer.Append(c);
                int closePos = wordBuffer.Length - 1;
                int openPos = wordBuffer.ToString().LastIndexOf('[', closePos - 1);
                string tagContent = wordBuffer.ToString(openPos + 1, closePos - openPos - 1);
                
                bool shouldEscape = false;
                if (tagContent != "/" && tagContent.Length > 0 && !tagContent.StartsWith("/"))
                {
                    if (tagContent.Length <= 1) shouldEscape = true;
                }
                
                if (!shouldEscape && tagContent != "/" && tagContent.Length > 0 && !tagContent.StartsWith("/"))
                {
                    string normalizedTag = NormalizeTagContent(tagContent);
                    if (normalizedTag != tagContent)
                    {
                        wordBuffer.Remove(openPos, closePos - openPos + 1);
                        wordBuffer.Length = openPos;
                        wordBuffer.Append("[");
                        wordBuffer.Append(normalizedTag);
                        wordBuffer.Append("]");
                        tagContent = normalizedTag;
                        closePos = wordBuffer.Length - 1;
                    }
                }
                
                if (tagContent == "/")
                {
                    string popped = openMarkupTags.Count > 0 ? openMarkupTags.Pop() : "<EMPTY>";
                    Console.WriteLine($"  CLOSE [/] — popped '{popped}', stack: [{string.Join(", ", openMarkupTags.Reverse())}], buf='{wordBuffer}'");
                }
                else if (tagContent.StartsWith("/"))
                {
                    string closeTagName = tagContent.Substring(1);
                    if (!string.IsNullOrEmpty(closeTagName) && openMarkupTags.Count > 0)
                    {
                        var tempStack = new Stack<string>();
                        bool found = false;
                        while (openMarkupTags.Count > 0)
                        {
                            string top = openMarkupTags.Pop();
                            if (string.Equals(top, closeTagName, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                            tempStack.Push(top);
                        }
                        while (tempStack.Count > 0) openMarkupTags.Push(tempStack.Pop());
                    }
                }
                else if (!string.IsNullOrEmpty(tagContent))
                {
                    openMarkupTags.Push(tagContent);
                    Console.WriteLine($"  OPEN [{tagContent}] — push, stack: [{string.Join(", ", openMarkupTags.Reverse())}], buf='{wordBuffer}'");
                }
                continue;
            }
            
            if (insideTag)
            {
                wordBuffer.Append(c);
                continue;
            }
            
            if (!insideTag && c == ']' && i + 1 < spectreMarkup.Length && spectreMarkup[i + 1] == ']')
            {
                wordBuffer.Append("]]");
                i++;
                visibleWordLen++;
                continue;
            }
            
            if (c == '\n')
            {
                wordBuffer.Clear();
                visibleWordLen = 0;
                currentLineLength = 0;
                continue;
            }
            
            if (char.IsWhiteSpace(c))
            {
                Console.WriteLine($"  SPACE — flushing buf='{wordBuffer}', visibleLen={visibleWordLen}, lineLen={currentLineLength}, stack: [{string.Join(", ", openMarkupTags.Reverse())}]");
                string word = wordBuffer.ToString();
                wordBuffer.Clear();
                visibleWordLen = 0;
                if (currentLineLength + word.Length + 1 <= availableWidth)
                {
                    currentLineLength += word.Length + 1;
                }
                else
                {
                    Console.WriteLine($"    → WRAP! WriteNewLine called. Stack: [{string.Join(", ", openMarkupTags.Reverse())}]");
                    currentLineLength = 1;
                }
                continue;
            }
            
            wordBuffer.Append(c);
            visibleWordLen++;
            
            int remaining = availableWidth - currentLineLength;
            if (visibleWordLen > remaining)
            {
                Console.WriteLine($"  MID-WORD WRAP: char='{c}', buf='{wordBuffer}', visibleLen={visibleWordLen}, remaining={remaining}, stack: [{string.Join(", ", openMarkupTags.Reverse())}]");
                string full = wordBuffer.ToString();
                int charsToEmit = Math.Min(remaining, full.Length);
                if (charsToEmit > 0)
                {
                    currentLineLength += Math.Min(charsToEmit, visibleWordLen);
                    wordBuffer.Clear();
                    wordBuffer.Append(full.Substring(charsToEmit));
                    visibleWordLen = Math.Max(0, visibleWordLen - Math.Min(charsToEmit, visibleWordLen));
                }
                if (wordBuffer.Length > 0)
                {
                    Console.WriteLine($"    → WriteNewLine after mid-word split. Stack: [{string.Join(", ", openMarkupTags.Reverse())}], remaining buf='{wordBuffer}'");
                }
                currentLineLength = 0;
            }
        }
        
        Console.WriteLine($"\n=== DONE === Stack: [{string.Join(", ", openMarkupTags.Reverse())}]");
    }
    
    static string NormalizeTagContent(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent)) return tagContent;
        int eqIdx = tagContent.IndexOf('=');
        if (eqIdx < 0) return tagContent;
        if (eqIdx > 0 && tagContent[eqIdx - 1] == ' ')
        {
            int trimEnd = eqIdx - 1;
            while (trimEnd >= 0 && tagContent[trimEnd] == ' ') trimEnd--;
            int trimStart = eqIdx + 1;
            while (trimStart < tagContent.Length && tagContent[trimStart] == ' ') trimStart++;
            string before = tagContent.Substring(0, trimEnd + 1);
            string after = tagContent.Substring(trimStart);
            return before + "=" + after;
        }
        return tagContent;
    }
}
