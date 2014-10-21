﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using JuliusSweetland.ETTA.Enums;
using JuliusSweetland.ETTA.Extensions;
using JuliusSweetland.ETTA.Models;
using JuliusSweetland.ETTA.Observables.PointAndKeyValueSources;
using JuliusSweetland.ETTA.Observables.TriggerSignalSources;
using JuliusSweetland.ETTA.Properties;
using JuliusSweetland.ETTA.Services;
using JuliusSweetland.ETTA.UI.ViewModels.Keyboards;
using JuliusSweetland.ETTA.UI.Views.Keyboards.English;
using log4net;
using Microsoft.Practices.Prism.Interactivity.InteractionRequest;
using Microsoft.Practices.Prism.Mvvm;
using Alpha = JuliusSweetland.ETTA.UI.ViewModels.Keyboards.Alpha;
using NumericAndSymbols1 = JuliusSweetland.ETTA.UI.ViewModels.Keyboards.NumericAndSymbols1;
using Publish = JuliusSweetland.ETTA.UI.ViewModels.Keyboards.Publish;
using Symbols2 = JuliusSweetland.ETTA.UI.ViewModels.Keyboards.Symbols2;
using YesNoQuestion = JuliusSweetland.ETTA.UI.ViewModels.Keyboards.YesNoQuestion;

namespace JuliusSweetland.ETTA.UI.ViewModels
{
    public class MainViewModel : BindableBase, IKeyboardStateManager
    {
        #region Fields

        private readonly static ILog Log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        
        private IInputService inputService;
        private readonly IOutputService outputService;
        private readonly NotifyingConcurrentDictionary<double> keySelectionProgress;
        private readonly NotifyingConcurrentDictionary<KeyDownStates> keyDownStates;
        private readonly KeyEnabledStates keyEnabledStates;
        private readonly InteractionRequest<Notification> errorNotificationRequest;

        private SelectionModes selectionMode;
        private Point? currentPositionPoint;
        private KeyValue? currentPositionKey;
        private Tuple<Point, double> pointSelectionProgress;
        
        #endregion

        #region Ctor

        public MainViewModel()
        {
            //TESTING START
            //Suggestions = new List<string>
            //{
            //    "Suggestion1", "AnotherOne", "OneMore", "Why not another", "And a final one", "Wait, one more"
            //};
            
            //Observable.Interval(TimeSpan.FromSeconds(3))
            //    .Take(1)
            //    .ObserveOnDispatcher()
            //    .Subscribe(i =>
            //    {
            //        ErrorNotificationRequest.Raise(new Notification
            //        {
            //            Title = "Uh-oh!",
            //            Content = "This is a test"
            //        });
            //        //Settings.Default.PublishingKeys = !Settings.Default.PublishingKeys;
            //    });
            //TESTING END

            inputService = CreateInputService();
            outputService = CreateOutputService();

            //Init readonly fields
            keySelectionProgress = new NotifyingConcurrentDictionary<double>();
            keyDownStates = new NotifyingConcurrentDictionary<KeyDownStates>();
            keyEnabledStates = new KeyEnabledStates(this);
            errorNotificationRequest = new InteractionRequest<Notification>();
            
            //Init state properties
            SelectionMode = SelectionModes.Key;
            //Keyboard = new YesNoQuestion(
            //        "This is a sample question. Let's see what happens as it gets longer. And longer, and longer and longer. Hmm - this should probably be wrapping by now.",
            //        () => Keyboard = new Alpha(),
            //        () => Keyboard = new Alpha());
            Keyboard = new Alpha();
            
            //Apply settings and subscribe to setting changes
            KeyDownStates[new KeyValue { FunctionKey = FunctionKeys.TogglePublish }.Key].Value =
                Settings.Default.PublishingKeys ? Enums.KeyDownStates.On : Enums.KeyDownStates.Off;

            Settings.Default.OnPropertyChanges(s => s.PublishingKeys)
                .Subscribe(pk => 
                    KeyDownStates[new KeyValue { FunctionKey = FunctionKeys.TogglePublish }.Key].Value =
                        pk ? Enums.KeyDownStates.On : Enums.KeyDownStates.Off);

            KeyDownStates[new KeyValue { FunctionKey = FunctionKeys.ToggleMultiKeySelectionSupported }.Key].Value =
                Settings.Default.MultiKeySelectionSupported ? Enums.KeyDownStates.On : Enums.KeyDownStates.Off;

            Settings.Default.OnPropertyChanges(s => s.MultiKeySelectionSupported)
                .Subscribe(mkss =>
                    KeyDownStates[new KeyValue { FunctionKey = FunctionKeys.ToggleMultiKeySelectionSupported }.Key].Value =
                        mkss ? Enums.KeyDownStates.On : Enums.KeyDownStates.Off);
            
            //Init input service properties
            inputService.KeyEnabledStates = keyEnabledStates;

            inputService.OnPropertyChanges(i => i.CapturingMultiKeySelection)
                .Subscribe(cmks => CapturingMultiKeySelection = cmks);

            inputService.PointsPerSecond += (o, value) =>
            {
                PointsPerSecond = value;
            };

            inputService.CurrentPosition += (o, tuple) =>
            {
                CurrentPositionPoint = tuple.Item1;
                CurrentPositionKey = tuple.Item2;
            };

            inputService.SelectionProgress += (o, progress) =>
            {
                if (progress.Item2 == 0)
                {
                    ResetSelectionProgress();
                }
                else if (progress.Item1 != null)
                {
                    if (SelectionMode == SelectionModes.Key
                        && progress.Item1.Value.KeyValue != null)
                    {
                        KeySelectionProgress[progress.Item1.Value.KeyValue.Value.Key] = new NotifyingProxy<double>(progress.Item2);
                    }
                    else if (SelectionMode == SelectionModes.Point)
                    {
                        PointSelectionProgress = new Tuple<Point, double>(progress.Item1.Value.Point, progress.Item2);
                    }
                }
            };

            inputService.Selection += (o, value) =>
            {
                SelectionResultPoints = null; //Clear captured points from previous SelectionResult event

                if (SelectionMode == SelectionModes.Key
                    && value.KeyValue != null)
                {
                    if (KeySelection != null)
                    {
                        KeySelection(this, value.KeyValue.Value);
                    }
                }
                else if (SelectionMode == SelectionModes.Point)
                {
                    //TODO: Handle point selection
                }
            };

            inputService.SelectionResult += (o, tuple) =>
            {
                var points = tuple.Item1;
                var singleKeyValue = tuple.Item2 != null || tuple.Item3 != null
                    ? new KeyValue {FunctionKey = tuple.Item2, String = tuple.Item3}
                    : (KeyValue?)null;
                var multiKeySelection = tuple.Item4;

                SelectionResultPoints = points; //Store captured points from SelectionResult event (displayed for debugging)

                if (SelectionMode == SelectionModes.Key
                    && (singleKeyValue != null || (multiKeySelection != null && multiKeySelection.Any())))
                {
                    KeySelectionResult(singleKeyValue, multiKeySelection);
                }
                else if (SelectionMode == SelectionModes.Point)
                {
                    //TODO: Handle point selection result
                }
            };

            inputService.Error += (o, exception) => 
                ErrorNotificationRequest.Raise(new Notification
                {
                    Title = "Uh-oh!",
                    Content = exception.Message
                });
        }

        #endregion

        #region Events

        public event EventHandler<KeyValue> KeySelection;

        #endregion

        #region Properties

        public IInputService InputService { get { return inputService; } }
        public IOutputService OutputService { get { return outputService; } }

        private IKeyboard keyboard;
        public IKeyboard Keyboard
        {
            get { return keyboard; }
            set { SetProperty(ref keyboard, value); }
        }

        public Dictionary<Rect, KeyValue> PointToKeyValueMap
        {
            set
            {
                inputService.PointToKeyValueMap = value;

                //The last selection result points cannot be valid if this has changed (window has moved or resized)
                SelectionResultPoints = null;
            }
        }

        public SelectionModes SelectionMode
        {
            get { return selectionMode; }
            set
            {
                if (SetProperty(ref selectionMode, value))
                {
                    ResetSelectionProgress();
                    InputService.SelectionMode = value;
                }
            }
        }

        private bool capturingMultiKeySelection;
        public bool CapturingMultiKeySelection
        {
            get { return capturingMultiKeySelection; }
            set { SetProperty(ref capturingMultiKeySelection, value); }
        }

        public Point? CurrentPositionPoint
        {
            get { return currentPositionPoint; }
            set { SetProperty(ref currentPositionPoint, value); }
        }

        public KeyValue? CurrentPositionKey
        {
            get { return currentPositionKey; }
            set { SetProperty(ref currentPositionKey, value); }
        }

        public Tuple<Point, double> PointSelectionProgress
        {
            get { return pointSelectionProgress; }
            private set
            {
                if (SetProperty(ref pointSelectionProgress, value))
                {
                    throw new NotImplementedException("Handling of PointSelection progress has not been implemented yet");
                }
            }
        }

        public NotifyingConcurrentDictionary<double> KeySelectionProgress
        {
            get { return keySelectionProgress; }
        }

        private List<Point> selectionResultPoints;
        public List<Point> SelectionResultPoints
        {
            get { return selectionResultPoints; }
            set { SetProperty(ref selectionResultPoints, value); }
        }

        private int pointsPerSecond;
        public int PointsPerSecond
        {
            get { return pointsPerSecond; }
            set { SetProperty(ref pointsPerSecond, value); }
        }

        public NotifyingConcurrentDictionary<KeyDownStates> KeyDownStates
        {
            get { return keyDownStates; }
        }

        public KeyEnabledStates KeyEnabledStates
        {
            get { return keyEnabledStates; }
        }

        private List<string> suggestions;
        public List<string> Suggestions
        {
            get { return suggestions; }
            set { SetProperty(ref suggestions, value); }
        }

        private int suggestionsPage;
        public int SuggestionsPage
        {
            get { return suggestionsPage; }
            set { SetProperty(ref suggestionsPage, value); }
        }

        private int suggestionsPerPage;
        public int SuggestionsPerPage
        {
            get { return suggestionsPerPage; }
            set { SetProperty(ref suggestionsPerPage, value); }
        }

        public InteractionRequest<Notification> ErrorNotificationRequest
        {
            get { return errorNotificationRequest; }
        }

        #endregion

        #region Methods

        private IInputService CreateInputService()
        {
            //Instantiate point source
            IPointAndKeyValueSource pointSource;
            switch (Settings.Default.PointsSource)
            {
                case PointsSources.GazeTracker:
                    pointSource = new GazeTrackerSource(
                        Settings.Default.PointTtl,
                        Settings.Default.GazeTrackerUdpPort,
                        new Regex(Settings.Default.GazeTrackerUdpRegex));
                    break;

                case PointsSources.TheEyeTribe:
                    pointSource = new TheEyeTribeSource(
                        Settings.Default.PointTtl,
                        new TheEyeTribePointService());
                    break;

                case PointsSources.MousePosition:
                    pointSource = new MousePositionSource(
                        Settings.Default.PointTtl);
                    break;

                default:
                    throw new ArgumentException(
                        "'PointsSource' settings is missing or not recognised! Please correct and restart ETTA.");
            }

            //Instantiate key trigger source
            ITriggerSignalSource keySelectionTriggerSource;
            switch (Settings.Default.KeySelectionTriggerSource)
            {
                case TriggerSources.AggregatedFixations:
                    keySelectionTriggerSource = new AggregateKeyFixationSource(
                        Settings.Default.KeySelectionTriggerFixationMinPoints,
                        Settings.Default.KeySelectionTriggerFixationTime,
                        Settings.Default.PointTtl,
                        pointSource.Sequence);
                    break;

                case TriggerSources.Fixations:
                    keySelectionTriggerSource = new KeyFixationSource(
                        Settings.Default.KeySelectionTriggerFixationMinPoints,
                        Settings.Default.KeySelectionTriggerFixationTime,
                        pointSource.Sequence);
                    break;

                case TriggerSources.KeyboardKeyDownsUps:
                    keySelectionTriggerSource = new KeyboardKeyDownUpSource(
                        Settings.Default.SelectionTriggerKeyboardKeyDownUpKey,
                        pointSource.Sequence);
                    break;

                case TriggerSources.MouseButtonDownUps:
                    keySelectionTriggerSource = new MouseButtonDownUpSource(
                        Settings.Default.SelectionTriggerMouseDownUpButton,
                        pointSource.Sequence);
                    break;

                default:
                    throw new ArgumentException(
                        "'KeySelectionTriggerSource' setting is missing or not recognised! Please correct and restart ETTA.");
            }

            //Instantiate point trigger source
            ITriggerSignalSource pointSelectionTriggerSource;
            switch (Settings.Default.PointSelectionTriggerSource)
            {
                case TriggerSources.AggregatedFixations:
                    throw new ArgumentException(
                        "'PointSelectionTriggerSource' setting is AggregatedFixations which is not supported! Please correct and restart ETTA.");

                case TriggerSources.Fixations:
                    pointSelectionTriggerSource = new PointFixationSource(
                        Settings.Default.PointSelectionTriggerFixationMinPoints,
                        Settings.Default.PointSelectionTriggerFixationRadius,
                        Settings.Default.PointSelectionTriggerFixationTime,
                        pointSource.Sequence);
                    break;

                case TriggerSources.KeyboardKeyDownsUps:
                    pointSelectionTriggerSource = new KeyboardKeyDownUpSource(
                        Settings.Default.SelectionTriggerKeyboardKeyDownUpKey,
                        pointSource.Sequence);
                    break;

                case TriggerSources.MouseButtonDownUps:
                    pointSelectionTriggerSource = new MouseButtonDownUpSource(
                        Settings.Default.SelectionTriggerMouseDownUpButton,
                        pointSource.Sequence);
                    break;

                default:
                    throw new ArgumentException(
                        "'PointSelectionTriggerSource' setting is missing or not recognised! "
                        + "Please correct and restart ETTA.");
            }

            //Instantiation dictionary and input services
            var dictionaryService = new DictionaryService();
            return new InputService(dictionaryService, pointSource, keySelectionTriggerSource, pointSelectionTriggerSource);
        }

        private IOutputService CreateOutputService()
        {
            return new OutputService(this);
        }

        private void KeySelectionResult(KeyValue? singleKeyValue, List<string> multiKeySelection)
        {
            //Single key string
            if (singleKeyValue != null
                && !string.IsNullOrEmpty(singleKeyValue.Value.String))
            {
                OutputService.ProcessCapture(singleKeyValue.Value.String);
            }

            //Single key function key
            if (singleKeyValue != null
                && singleKeyValue.Value.FunctionKey != null)
            {
                HandleFunctionKeySelectionResult(singleKeyValue);
            }

            //Multi key selection
            if (multiKeySelection != null
                && multiKeySelection.Any())
            {
                OutputService.ProcessCapture(multiKeySelection.First());
                Suggestions = multiKeySelection.Skip(1).ToList();
            }
        }

        private void HandleFunctionKeySelectionResult(KeyValue? singleKeyValue)
        {
            switch (singleKeyValue.Value.FunctionKey.Value)
            {
                case FunctionKeys.AlphaKeyboard:
                    Keyboard = new Alpha();
                    break;

                case FunctionKeys.BackMany:
                    OutputService.ProcessBackMany();
                    break;

                case FunctionKeys.BackOne:
                    OutputService.ProcessBackOne();
                    break;

                case FunctionKeys.ClearOutput:
                    OutputService.ClearText();
                    break;

                case FunctionKeys.NoQuestionResult:
                    HandleYesNoQuestionResult(false);
                    break;

                case FunctionKeys.NumericAndSymbols1Keyboard:
                    Keyboard = new NumericAndSymbols1();
                    break;

                case FunctionKeys.NextSuggestions:
                    if (Suggestions != null
                        && (Suggestions.Count > (SuggestionsPage + 1)*SuggestionsPerPage))
                    {
                        SuggestionsPage++;
                    }
                    break;

                case FunctionKeys.PreviousSuggestions:
                    if (SuggestionsPage > 0)
                    {
                        SuggestionsPage--;
                    }
                    break;

                case FunctionKeys.PublishKeyboard:
                    Keyboard = new Publish();
                    break;

                case FunctionKeys.Shift:
                    var shiftKey = new KeyValue {FunctionKey = FunctionKeys.Shift}.Key;
                    KeyDownStates[shiftKey].Value =
                        KeyDownStates[shiftKey].Value == Enums.KeyDownStates.Off
                            ? KeyDownStates[shiftKey].Value = Enums.KeyDownStates.On
                            : KeyDownStates[shiftKey].Value == Enums.KeyDownStates.On
                                ? KeyDownStates[shiftKey].Value = Enums.KeyDownStates.Lock
                                : KeyDownStates[shiftKey].Value = Enums.KeyDownStates.Off;
                    break;

                case FunctionKeys.Suggestion1:
                    PassSuggestionToOutputService(0);
                    break;

                case FunctionKeys.Suggestion2:
                    PassSuggestionToOutputService(1);
                    break;

                case FunctionKeys.Suggestion3:
                    PassSuggestionToOutputService(2);
                    break;

                case FunctionKeys.Suggestion4:
                    PassSuggestionToOutputService(3);
                    break;

                case FunctionKeys.Suggestion5:
                    PassSuggestionToOutputService(4);
                    break;

                case FunctionKeys.Suggestion6:
                    PassSuggestionToOutputService(5);
                    break;

                case FunctionKeys.Symbols2Keyboard:
                    Keyboard = new Symbols2();
                    break;

                case FunctionKeys.ToggleMultiKeySelectionSupported:
                    Settings.Default.MultiKeySelectionSupported = !Settings.Default.MultiKeySelectionSupported;
                    break;

                case FunctionKeys.TogglePublish:
                    Settings.Default.PublishingKeys = !Settings.Default.PublishingKeys;
                    break;

                case FunctionKeys.YesQuestionResult:
                    HandleYesNoQuestionResult(true);
                    break;
            }

            OutputService.ProcessCapture(singleKeyValue.Value.FunctionKey.Value);
        }

        private void PassSuggestionToOutputService(int keyIndex)
        {
            var suggestionIndex = (SuggestionsPage * SuggestionsPerPage) + keyIndex;
            if (Suggestions.Count > suggestionIndex)
            {
                OutputService.SwapLastCaptureForSuggestion(Suggestions[suggestionIndex]);
            }
        }

        private void HandleYesNoQuestionResult(bool yesResult)
        {
            var yesNoQuestion = Keyboard as YesNoQuestion;
            if (yesNoQuestion != null)
            {
                if (yesResult)
                {
                    yesNoQuestion.YesAction();
                }
                else
                {
                    yesNoQuestion.NoAction();
                }
            }
        }

        private void ResetSelectionProgress()
        {
            PointSelectionProgress = null;
            KeySelectionProgress.Clear();
        }

        #endregion
    }
}