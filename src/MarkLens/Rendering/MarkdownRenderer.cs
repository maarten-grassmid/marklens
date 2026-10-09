using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MarkLens.Rendering
{
    internal sealed class RenderOptions
    {
        public bool Math;
        public bool Mermaid;
        public bool Emoji;

        public override bool Equals(object obj)
            => obj is RenderOptions o && o.Math == Math && o.Mermaid == Mermaid && o.Emoji == Emoji;

        public override int GetHashCode() => (Math ? 1 : 0) | (Mermaid ? 2 : 0) | (Emoji ? 4 : 0);
    }

    internal sealed class RenderResult
    {
        public string Html;
        public int LineCount;
    }

    /// <summary>
    /// Converts Markdown to HTML with Markdig. Every block element is annotated with
    /// data-line / data-line-end (0-based source lines) so the viewer can map rendered
    /// content back to the original Markdown for scroll sync, "edit selection" and task toggles.
    /// </summary>
    internal static class MarkdownRenderer
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<RenderOptions, MarkdownPipeline> Pipelines = new Dictionary<RenderOptions, MarkdownPipeline>();

        public static MarkdownPipeline GetPipeline(RenderOptions options)
        {
            lock (Gate)
            {
                if (Pipelines.TryGetValue(options, out var p))
                    return p;

                var b = new MarkdownPipelineBuilder()
                    .UsePreciseSourceLocation()
                    .UseAlertBlocks()
                    .UseAbbreviations()
                    .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
                    .UseCitations()
                    .UseCustomContainers()
                    .UseDefinitionLists()
                    .UseEmphasisExtras()
                    .UseFigures()
                    .UseFooters()
                    .UseFootnotes()
                    .UseGridTables()
                    .UseMediaLinks()
                    .UsePipeTables()
                    .UseListExtras()
                    .UseTaskLists()
                    .UseAutoLinks()
                    .UseYamlFrontMatter();

                if (options.Math)
                    b.UseMathematics();
                if (options.Mermaid)
                    b.UseDiagrams();
                if (options.Emoji)
                    b.UseEmojiAndSmiley(enableSmileys: false);

                // Must be last: lets {#id .class} attributes apply to any element.
                b.UseGenericAttributes();

                p = b.Build();
                Pipelines[options] = p;
                return p;
            }
        }

        public static RenderResult Render(string markdown, RenderOptions options)
        {
            markdown = markdown ?? string.Empty;
            var pipeline = GetPipeline(options);
            var document = Markdown.Parse(markdown, pipeline);
            var lineStarts = ComputeLineStarts(markdown);

            foreach (var block in document.Descendants<Block>())
            {
                if (block is YamlFrontMatterBlock)
                    continue;
                if (block.Span.IsEmpty)
                    continue;

                int startLine = block.Line;
                int endLine = LineOf(lineStarts, Math.Max(block.Span.Start, block.Span.End));
                if (endLine < startLine)
                    endLine = startLine;

                var attrs = block.GetAttributes();
                attrs.AddProperty("data-line", startLine.ToString(CultureInfo.InvariantCulture));
                attrs.AddProperty("data-line-end", endLine.ToString(CultureInfo.InvariantCulture));
            }

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                var renderer = new HtmlRenderer(writer);
                pipeline.Setup(renderer);
                renderer.Render(document);
                writer.Flush();
                return new RenderResult { Html = writer.ToString(), LineCount = lineStarts.Count };
            }
        }

        private static List<int> ComputeLineStarts(string text)
        {
            var starts = new List<int>(Math.Max(16, text.Length / 40)) { 0 };
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    starts.Add(i + 1);
                }
                else if (c == '\n')
                {
                    starts.Add(i + 1);
                }
            }
            return starts;
        }

        private static int LineOf(List<int> lineStarts, int offset)
        {
            int idx = lineStarts.BinarySearch(offset);
            return idx >= 0 ? idx : ~idx - 1;
        }
    }
}
