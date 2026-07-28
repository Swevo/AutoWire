using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AutoWire.VisualStudio.Adornment
{
    /// <summary>MEF export wiring up <see cref="RegistrationAdornmentTagger"/> for C# text views.</summary>
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("CSharp")]
    [TagType(typeof(IntraTextAdornmentTag))]
    internal sealed class RegistrationAdornmentTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView is null || buffer is null || textView.TextBuffer != buffer) return null;

            return textView.Properties.GetOrCreateSingletonProperty(
                typeof(RegistrationAdornmentTagger),
                () => new RegistrationAdornmentTagger(buffer)) as ITagger<T>;
        }
    }
}
