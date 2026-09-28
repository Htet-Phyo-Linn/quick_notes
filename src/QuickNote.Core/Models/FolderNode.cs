using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickNote.Core.Models
{
    public class FolderNode : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _title = string.Empty;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DisplayName => !string.IsNullOrWhiteSpace(Title) ? Title : Name;

        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _editingText = string.Empty;
        public string EditingText
        {
            get => _editingText;
            set
            {
                if (_editingText != value)
                {
                    _editingText = value;
                    OnPropertyChanged();
                }
            }
        }

        public NoteDocument? Note { get; set; }

        public ObservableCollection<FolderNode> Children { get; set; } = new ObservableCollection<FolderNode>();

        public bool IsDescendantOf(FolderNode potentialAncestor)
        {
            if (potentialAncestor == null || string.IsNullOrWhiteSpace(potentialAncestor.FullPath)) return false;
            var ancestorPath = potentialAncestor.FullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            return FullPath.StartsWith(ancestorPath, StringComparison.OrdinalIgnoreCase);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => DisplayName;
    }
}
