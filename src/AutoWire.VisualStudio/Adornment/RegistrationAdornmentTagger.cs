using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;

namespace AutoWire.VisualStudio.Adornment
{
    /// <summary>
    /// Produces a small gray "AutoWire: Lifetime → IFoo, IBar" <see cref="IntraTextAdornmentTag"/> immediately
    /// after the opening brace of any class decorated with an AutoWire registration attribute. Re-parses the
    /// whole buffer (cheaply, via regex) whenever its snapshot version changes; never throws into the editor
    /// pipeline — parse/render failures simply result in no adornments being shown.
    /// </summary>
    internal sealed class RegistrationAdornmentTagger : ITagger<IntraTextAdornmentTag>, IDisposable
    {
        private readonly ITextBuffer _buffer;
        private ITextSnapshot _cachedSnapshot;
        private IReadOnlyList<RegistrationHint> _cachedHints = Array.Empty<RegistrationHint>();

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public RegistrationAdornmentTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(e.After, 0, e.After.Length)));
        }

        public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) yield break;

            var snapshot = spans[0].Snapshot;
            EnsureParsed(snapshot);

            foreach (var hint in _cachedHints)
            {
                if (hint.InsertionPosition < 0 || hint.InsertionPosition > snapshot.Length) continue;

                var point = new SnapshotPoint(snapshot, hint.InsertionPosition);
                var span = new SnapshotSpan(point, 0);
                var overlaps = false;
                foreach (var requested in spans)
                {
                    if (requested.Start <= point && point <= requested.End) { overlaps = true; break; }
                }
                if (!overlaps) continue;

                UIElement visual;
                try
                {
                    visual = CreateAdornmentVisual(hint.Text);
                }
                catch
                {
                    continue; // Never let a rendering failure break tagging for the rest of the document.
                }

                yield return new TagSpan<IntraTextAdornmentTag>(span, new IntraTextAdornmentTag(visual, null));
            }
        }

        private void EnsureParsed(ITextSnapshot snapshot)
        {
            if (ReferenceEquals(_cachedSnapshot, snapshot)) return;

            try
            {
                _cachedHints = AutoWireRegistrationParser.Parse(snapshot.GetText());
            }
            catch
            {
                _cachedHints = Array.Empty<RegistrationHint>();
            }

            _cachedSnapshot = snapshot;
        }

        private static UIElement CreateAdornmentVisual(string text) => new TextBlock
        {
            Text = "  " + text,
            Foreground = Brushes.Gray,
            FontStyle = FontStyles.Italic,
            FontSize = 10.5,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        public void Dispose() => _buffer.Changed -= OnBufferChanged;
    }
}
