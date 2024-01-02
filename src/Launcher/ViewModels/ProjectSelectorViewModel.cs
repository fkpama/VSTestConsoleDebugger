using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Threading;
using Sodiware.IO;
using ViewModel = Sodiware.VisualStudio.PlatformUI.ViewModel;

namespace Launcher.ViewModels
{
    public interface ILaunchProfileSaver
    {
        Task SaveAsync(Target target, CancellationToken cancellationToken);
        bool HasProfile(Target target);
    }

    internal enum ControlAction
    {
        None  = 0,
        CloseWindow = 1
    }

    public sealed class ProjectSelectorViewModel : ViewModel, INotifyDataErrorInfo
    {
        #region Fields

        private readonly Dictionary<string, List<string>> m_errors = new(StringComparer.Ordinal);
        private ILaunchProfileSaver? launchProfileSaver;
        private ObservableCollection<EntryViewModel> entries;
        private ReadOnlyObservableCollection<EntryViewModel>? roEntries;
        private event EventHandler<DataErrorsChangedEventArgs>? errorsChanged;

        #endregion Fields

        #region Properties

        public ICommand? OkCommand { get; private set; }
        public ICommand? CancelCommand { get; private set; }
        public ICommand DeleteExeEntryCommand { get; private set; }
        public ICommand SaveAsProfileCommand { get; private set; }
        public string SelectionModeLabel { get; } = "Select a project or enter an executable to launch:";
        public bool HasMultipleProjects { get; private set; }
        public ILaunchProfileSaver? LaunchProfileSaver
        {
            get => this.launchProfileSaver;
            set
            {
                if (SetProperty(ref this.launchProfileSaver, value))
                {
                    this.updateCanSaveProfile();
                }
            }
        }

        #region Action property

        private ProjectSelectorAction m_Action;
        /// <summary>
        /// Action property
        /// <summary>
        public ProjectSelectorAction Action
        {
            get => m_Action;
            set => this.SetProperty(ref m_Action, value);
        }

        #endregion Action property

        #region InvalidTargetErrorMessage property

        private string? m_InvalidTargetErrorMessage;
        /// <summary>
        /// InvalidTargetErrorMessage property
        /// <summary>
        public string? InvalidTargetErrorMessage
        {
            get => m_InvalidTargetErrorMessage;
            set => this.SetProperty(ref m_InvalidTargetErrorMessage, value);
        }

        #endregion InvalidTargetErrorMessage property

        #region IsValidExecutable property

        private bool m_IsValidExecutable = true;
        /// <summary>
        /// IsValidExecutable property
        /// <summary>
        public bool IsValidExecutable
        {
            get => m_IsValidExecutable;
            set
            {
                if (this.SetProperty(ref m_IsValidExecutable, value))
                    this.IsInvalidExecutable = !value;
            }
        }

        #endregion IsValidExecutable property

        #region IsInvalidExecutable property

        private bool m_IsInvalidExecutable;
        /// <summary>
        /// IsInvalidExecutable property
        /// <summary>
        public bool IsInvalidExecutable
        {
            get => m_IsInvalidExecutable;
            set
            {
                if (this.SetProperty(ref m_IsInvalidExecutable, value))
                    this.IsValidExecutable = !value;
            }
        }

        #endregion IsInvalidExecutable property

        #region SelectedExecutable property

        //private string? m_SelectedExecutable;
        /// <summary>
        /// SelectedExecutable property
        /// <summary>
        public string? SelectedExecutable
        {
            get
            {
                if (this.SelectedEntry?.Type != ProjectSelectorAction.Executable)
                    return null;
                return this.SelectedEntry.TargetPath;
            }
            set
            {
                if (value.IsMissing())
                {
                    this.SelectedEntry = null;
                    return;
                }
                var entry = this.entries
                    .FirstOrDefault(x => x.Type == ProjectSelectorAction.Executable && PathUtils.IsSamePath(x.TargetPath, value));
                if (entry is null)
                {
                    Assumes.NotNullOrWhitespace(value);
                    entry = new EntryViewModel(value);
                }
                if (entry != this.m_SelectedEntry)
                {
                    this.m_SelectedEntry = entry;
                    RaisePropertyChanged();
                    this.checkIsValidExecutable(value);
                }
                this.updateCanSaveProfile();
            }
        }

        #endregion SelectedExecutable property

        #region SelectedEntry property

        private EntryViewModel? m_SelectedEntry;
        /// <summary>
        /// SelectedEntry property
        /// <summary>
        public EntryViewModel? SelectedEntry
        {
            get => m_SelectedEntry;
            set
            {
                if (this.SetProperty(ref m_SelectedEntry, value))
                {
                    if (this.Mode == ProjectSelectorAction.Executable)
                    {
                        this.RaisePropertyChanged(nameof(SelectedExecutable));
                    }
                    this.updateCanSaveProfile();
                }
            }
        }

        #endregion SelectedEntry property

        #region CanSaveProfile property

        private bool m_CanSaveProfile;
        /// <summary>
        /// CanSaveProfile property
        /// <summary>
        public bool CanSaveProfile
        {
            get => m_CanSaveProfile;
            set => this.SetProperty(ref m_CanSaveProfile, value);
        }

        #endregion CanSaveProfile property

        #region Mode property

        private ProjectSelectorAction m_Mode;

        /// <summary>
        /// Mode property
        /// <summary>
        public ProjectSelectorAction Mode
        {
            get => m_Mode;
            set => this.SetProperty(ref m_Mode, value);
        }

        #endregion Mode property

        public Collection<EntryViewModel> Projects { get; private set; }
        //public CollectionViewSource EntriesSource { get; private set; }
        public ReadOnlyObservableCollection<EntryViewModel> Entries
        {
            get => this.roEntries ??= new(this.entries);
        }
        public bool Cancelled { get; internal set; }


        #endregion Properties

        #region Constructors

        public ProjectSelectorViewModel()
        {
            this.initializeCommands(null);
            fillEntries();
        }

        public ProjectSelectorViewModel(JoinableTaskFactory jtf,
                                        IList<EntryViewModel> entries,
                                        IList<EntryViewModel> models,
                                        IEnumerable<string> mruList,
                                        ILaunchProfileSaver? launchProfileSaver = null)
        {
            this.entries = new(entries);
            if (entries.Count > 0)
            {
                var lastEntry = entries[0];
                this.Mode = lastEntry.Type;
                if (lastEntry.Type == ProjectSelectorAction.Executable)
                {
                    this.SelectedExecutable = lastEntry.TargetPath;
                }
                else
                {
                    var found = models.FirstOrDefault(x => x.ProjectId == lastEntry.ProjectId);
                    this.SelectedEntry = found;
                }
            }
            this.Projects = new(models);
            this.HasMultipleProjects = this.Projects.Count > 1;
            this.HasMultipleProjects = this.Projects.Count > 0;
            this.initializeCommands(jtf);
            this.launchProfileSaver = launchProfileSaver;
        }

        #endregion Constructors

        #region Public methods

        public void Add(string path)
        {
            this.entries.Add(new(path));
        }

        #endregion Public methods

        #region Internal methods

        internal bool HandleProjectDoubleClick()
        {
            return this.CanSaveProfile && Utils.IsVsIde;
        }

        internal ControlAction SelectEntry(EntryViewModel entry)
        {
            this.SelectedEntry = entry;
            return ControlAction.CloseWindow;
        }

        #endregion Internal methods

        #region Private methods

        [MemberNotNull(nameof(OkCommand),
            nameof(CancelCommand),
            nameof(DeleteExeEntryCommand),
            nameof(SaveAsProfileCommand))]
        private void initializeCommands(JoinableTaskFactory? jtf)
        {
            this.OkCommand = new DelegateCommand<UIElement>(element =>
            {
                element.FindAncestor<Window>()?.Close();
            }, canClickOk, jtf);
            this.CancelCommand = new DelegateCommand<UIElement>(element =>
            {
                this.Cancelled = true;
                element.FindAncestor<Window>()?.Close();
                throw new OperationCanceledException("User cancelled");
            }, x => true, jtf);
            this.DeleteExeEntryCommand = new DelegateCommand<EntryViewModel>(vm =>
            {
                this.entries.Remove(vm);
            }, x => true, jtf);

            this.SaveAsProfileCommand = new DelegateCommand(vm =>
            {
                Assumes.NotNull(this.launchProfileSaver);
                var entry = this.SelectedEntry;
                if (entry is null)
                {
                    return;
                }
                this.launchProfileSaver
                .SaveAsync(entry.GetEntry(), CancellationToken.None)
                .Forget();
            }, x => this.CanSaveProfile, jtf);
        }

        private bool canClickOk(UIElement? obj = null)
        {
            bool canSaveProfile;
            if (this.Mode == ProjectSelectorAction.Executable)
            {
                canSaveProfile = this.SelectedExecutable.IsPresent()
                    && this.IsValidExecutable;
            }
            else
            {
                canSaveProfile = this.SelectedEntry is not null;
            }
            return canSaveProfile;
        }

        private bool updateCanSaveProfile()
        {
            var result = false;
            if (this.launchProfileSaver is null)
                return false;
            if (this.SelectedEntry?.IsValid == true)
            {
                if (!this.launchProfileSaver.HasProfile(this.SelectedEntry.GetEntry()))
                {
                    result = true;
                }
            }

            this.CanSaveProfile = result;
            return result;
        }

        private bool checkIsValidExecutable(string? path)
        {
            var result = false;
            string? message = null;
            var wasNull = this.m_SelectedEntry is null;
            if (!this.m_errors.TryGetValue(nameof(SelectedExecutable), out var exe))
            {
                exe = new();
                this.m_errors[nameof(SelectedExecutable)] = exe;
            }
            var hadErrors = exe.Count > 0;
            exe.Clear();

            path = path?.Trim().Trim('\"');
            if (path.IsMissing())
            {
                result = true;
            }
            else if (!PathUtils.IsValidPath(path))
            {
                message = "Selected path is not valid";
            }
            else if (!File.Exists(path))
            {
                message = $"The file does not exists";
            }
            else
            {
                result = true;
                message = null;
            }

            this.IsValidExecutable = result;
            this.InvalidTargetErrorMessage = message;
            var hasError = message is not null;
            if (message is not null)
            {
                exe.Add(message);
            }
            if (hadErrors != hasError || wasNull)
            {
                this.errorsChanged?.Invoke(this, new(nameof(SelectedExecutable)));
                if (wasNull)
                    ((DelegateCommandBase?)this.OkCommand)?.RaiseCanExecuteChanged();
                //CommandManager.InvalidateRequerySuggested();
            }
            return result;
        }

        [Conditional("DEBUG")]
        [MemberNotNull(nameof(entries), nameof(Projects))]
        private void fillEntries()
        {
            if (!Utils.IsVsIde)
            {
                this.entries = new()
                {
                        new(new Target(ProjectSelectorAction.Project, "Entry Project 1", Guid.NewGuid())),
                        new(new Target(ProjectSelectorAction.Executable, "C:\\Entry Path1")),
                };
                var models = new EntryViewModel[]
                {
                    new("Project 1", Guid.NewGuid()),
                    new("Project 1", Guid.NewGuid()),
                    new("Project 2", Guid.NewGuid())
                };
                this.Projects = new(models);
                this.HasMultipleProjects = true;
            }
            else
            {
                this.Projects = new();
                this.entries = new();
            }
        }

        #endregion Private methods

        #region INotifyDataErrorInfo implementation
        public bool HasErrors => this.m_errors.Any(x => x.Value.Count > 0);

        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged
        {
            add => errorsChanged += value;
            remove => errorsChanged += value;
        }

        IEnumerable INotifyDataErrorInfo.GetErrors(string propertyName)
        {
            return this.m_errors.TryGetValue(propertyName, out var lst)
                ? lst
                : Enumerable.Empty<string>();
        }

        internal string GetPath()
        {
            if (this.Mode == ProjectSelectorAction.Executable)
            {
                Assumes.NotNull(this.SelectedExecutable);
                return this.SelectedExecutable;
            }
            else
            {
                Assumes.NotNull(this.SelectedEntry);
                Assumes.NotNull(this.SelectedEntry.TargetPath);
                return this.SelectedEntry.TargetPath;
            }
        }

        #endregion INotifyDataErrorInfo implementation
    }
}
