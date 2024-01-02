using System.Windows.Media.Imaging;

namespace Launcher.ViewModels
{
    public sealed class EntryViewModel : ViewModel
    {
        Target entry;

        public Guid? ProjectId => entry.Id;
        public string TargetPath => entry.TargetPath;
        public ProjectSelectorAction Type => entry.Mode;
        public BitmapSource? Bitmap { get; set; }

        internal bool IsValid
        {
            get
            {
                return this.Type == ProjectSelectorAction.Project
                    || (this.TargetPath.IsPresent() && File.Exists(this.TargetPath));
            }
        }


        #region HasDisplayText property

        private bool m_HasDisplayText;
        /// <summary>
        /// HasDisplayText property
        /// <summary>
        public bool HasDisplayText
        {
            get => m_HasDisplayText;
            private set => this.SetProperty(ref m_HasDisplayText, value);
        }

        #endregion HasDisplayText property

        #region DisplayText property

        private string? m_DisplayText;
        /// <summary>
        /// DisplayText property
        /// <summary>
        public string? DisplayText
        {
            get => m_DisplayText.IfMissing(this.TargetPath);
            internal set
            {
                if (value.IsMissing())
                    value = null;
                if(this.SetProperty(ref m_DisplayText, value))
                {
                    this.HasDisplayText = value is not null;
                }
            }
        }

        #endregion DisplayText property

        internal EntryViewModel() { }
        internal EntryViewModel(string path, Guid projectId)
        {
            Guard.Debug.NotNullOrWhitespace(path);
            this.entry = new(ProjectSelectorAction.Project, path, projectId);
        }
        internal EntryViewModel(string path)
        {
            Guard.Debug.NotNullOrWhitespace(path);
            this.entry = new(ProjectSelectorAction.Executable, path);
        }
        internal EntryViewModel(Target result)
        {
            this.entry = result;
        }
        public override string ToString()
            => this.TargetPath ?? base.ToString();

        internal Target GetEntry()
        {
            return this.entry;
        }
    }
}
