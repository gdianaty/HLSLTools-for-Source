﻿using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Outlining;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using ShaderTools.CodeAnalysis;
using ShaderTools.CodeAnalysis.Options;
using ShaderTools.VisualStudio.LanguageServices;

namespace ShaderTools.Editor.VisualStudio.Hlsl.Tagging.Outlining
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(HlslConstants.ContentTypeName)]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal sealed class OutliningTextViewCreationListener : IVsTextViewCreationListener
    {
        [Import]
        public IVsEditorAdaptersFactoryService EditorAdaptersFactoryService { get; set; }

        [Import]
        public IOutliningManagerService OutliningManagerService { get; set; }

        [Import]
        public VisualStudioWorkspace Workspace { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var textView = EditorAdaptersFactoryService.GetWpfTextView(textViewAdapter);

            var outliningManager = OutliningManagerService.GetOutliningManager(textView);

            if (!Workspace.Options.GetOption(FeatureOnOffOptions.Outlining, LanguageNames.Hlsl))
                outliningManager.Enabled = false;

            textView.Properties.GetOrCreateSingletonProperty(() => new OutliningCommandTarget(textViewAdapter, textView, OutliningManagerService));
        }
    }
}