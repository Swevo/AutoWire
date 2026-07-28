using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace AutoWire.VisualStudio.Adornment
{
    /// <summary>
    /// Eagerly materializes the <see cref="RegistrationAdornmentTagger"/> singleton as soon as a C# document
    /// view is created, so registration hints appear immediately rather than only once some other feature
    /// happens to request an <see cref="IntraTextAdornmentTag"/> tagger for the buffer.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("CSharp")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class RegistrationAdornmentTextViewCreationListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView is null) return;

            textView.Properties.GetOrCreateSingletonProperty(
                typeof(RegistrationAdornmentTagger),
                () => new RegistrationAdornmentTagger(textView.TextBuffer));
        }
    }
}
