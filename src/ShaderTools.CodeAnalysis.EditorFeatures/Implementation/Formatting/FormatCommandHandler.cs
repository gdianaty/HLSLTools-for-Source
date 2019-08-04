﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using ShaderTools.CodeAnalysis.Editor.Properties;
using ShaderTools.CodeAnalysis.Editor.Shared.Extensions;
using ShaderTools.CodeAnalysis.Editor.Shared.Utilities;
using ShaderTools.CodeAnalysis.Shared.Extensions;
using ShaderTools.CodeAnalysis.Text;
using ShaderTools.Utilities.Threading;

namespace ShaderTools.CodeAnalysis.Editor.Implementation.Formatting
{
    [Export(typeof(ICommandHandler))]
    [ContentType(ContentTypeNames.ShaderToolsContentType)]
    [Order(Before = PredefinedCompletionNames.CompletionCommandHandler)]
    [Name(nameof(FormatCommandHandler))]
    internal partial class FormatCommandHandler :
        ICommandHandler<FormatDocumentCommandArgs>,
        ICommandHandler<FormatSelectionCommandArgs>,
        IChainedCommandHandler<PasteCommandArgs>,
        IChainedCommandHandler<TypeCharCommandArgs>,
        IChainedCommandHandler<ReturnKeyCommandArgs>
    {
        private readonly ITextUndoHistoryRegistry _undoHistoryRegistry;
        private readonly IEditorOperationsFactoryService _editorOperationsFactoryService;

        public string DisplayName => "Format";

        [ImportingConstructor]
        public FormatCommandHandler(
            ITextUndoHistoryRegistry undoHistoryRegistry,
            IEditorOperationsFactoryService editorOperationsFactoryService)
        {
            _undoHistoryRegistry = undoHistoryRegistry;
            _editorOperationsFactoryService = editorOperationsFactoryService;
        }

        private void Format(ITextView textView, Document document, TextSpan? selectionOpt, CancellationToken cancellationToken)
        {
            var formattingService = document.GetLanguageService<IEditorFormattingService>();

            //using (Logger.LogBlock(FunctionId.CommandHandler_FormatCommand, KeyValueLogMessage.Create(LogType.UserAction, m => m["Span"] = selectionOpt?.Length ?? -1), cancellationToken))
            using (var transaction = new CaretPreservingEditTransaction(EditorFeaturesResources.Formatting, textView, _undoHistoryRegistry, _editorOperationsFactoryService))
            {
                var changes = formattingService.GetFormattingChangesAsync(document, selectionOpt, cancellationToken).WaitAndGetResult(cancellationToken);
                if (changes.Count == 0)
                {
                    return;
                }

                //using (Logger.LogBlock(FunctionId.Formatting_ApplyResultToBuffer, cancellationToken))
                {
                    document.Workspace.ApplyTextChanges(document.Id, changes, cancellationToken);
                }

                transaction.Complete();
            }
        }

        private static CommandState GetCommandState(ITextBuffer buffer)
        {
            //if (!buffer.CanApplyChangeDocumentToWorkspace())
            //{
            //    return nextHandler();
            //}

            return CommandState.Available;
        }

        private void ExecuteReturnOrTypeCommand(EditorCommandArgs args, Action nextHandler, CancellationToken cancellationToken)
        {
            // This method handles only return / type char
            if (!(args is ReturnKeyCommandArgs || args is TypeCharCommandArgs))
            {
                return;
            }

            // run next handler first so that editor has chance to put the return into the buffer first.
            nextHandler();

            var textView = args.TextView;
            var subjectBuffer = args.SubjectBuffer;
            //if (!subjectBuffer.CanApplyChangeDocumentToWorkspace())
            //{
            //    return;
            //}

            var caretPosition = textView.GetCaretPoint(args.SubjectBuffer);
            if (!caretPosition.HasValue)
            {
                return;
            }

            var document = subjectBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                return;
            }

            var service = document.GetLanguageService<IEditorFormattingService>();
            if (service == null)
            {
                return;
            }

            // save current caret position
            var caretPositionMarker = new SnapshotPoint(args.SubjectBuffer.CurrentSnapshot, caretPosition.Value);
            if (args is ReturnKeyCommandArgs)
            {
                if (!service.SupportsFormatOnReturn ||
                    !TryFormat(textView, document, service, ' ', caretPositionMarker, formatOnReturn: true, cancellationToken: cancellationToken))
                {
                    return;
                }
            }
            else if (args is TypeCharCommandArgs typeCharArgs)
            {
                var typedChar = typeCharArgs.TypedChar;
                if (!service.SupportsFormattingOnTypedCharacter(document, typedChar) ||
                    !TryFormat(textView, document, service, typedChar, caretPositionMarker, formatOnReturn: false, cancellationToken: cancellationToken))
                {
                    return;
                }
            }

            // get new caret position after formatting
            var newCaretPositionMarker = args.TextView.GetCaretPoint(args.SubjectBuffer);
            if (!newCaretPositionMarker.HasValue)
            {
                return;
            }

            var snapshotAfterFormatting = args.SubjectBuffer.CurrentSnapshot;

            var oldCaretPosition = caretPositionMarker.TranslateTo(snapshotAfterFormatting, PointTrackingMode.Negative);
            var newCaretPosition = newCaretPositionMarker.Value.TranslateTo(snapshotAfterFormatting, PointTrackingMode.Negative);
            if (oldCaretPosition.Position == newCaretPosition.Position)
            {
                return;
            }

            // caret has moved to wrong position, move it back to correct position
            args.TextView.TryMoveCaretToAndEnsureVisible(oldCaretPosition);
        }
    }
}
