using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuickNote.Core.Models;

namespace QuickNote.App.ViewModels
{
    public class NoteTabViewModel : INotifyPropertyChanged
    {
        private NoteDocument _note;
        private string _title = string.Empty;
        private bool _isActive;
        private bool _isModified;

        public NoteTabViewModel(NoteDocument note)
        {
            _note = note;
            _title = !string.IsNullOrWhiteSpace(note.Title) ? note.Title : note.FileName;
        }

        public NoteDocument Note
        {
            get => _note;
            set
            {
                if (_note != value)
                {
                    _note = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FullPath));
                    Title = !string.IsNullOrWhiteSpace(_note.Title) ? _note.Title : _note.FileName;
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
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string DisplayTitle => FormatWebTitle(_title);

        /// <summary>
        /// Web-design style title truncation by word count and length (e.g. "broker connectivity ...").
        /// Preserves full words rather than chopping words mid-character.
        /// </summary>
        public static string FormatWebTitle(string rawTitle, int maxWords = 4, int maxChars = 26)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return "Untitled";
            string trimmed = rawTitle.Trim();

            string[] words = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "Untitled";

            // If it fits within limits, return as is
            if (words.Length <= maxWords && trimmed.Length <= maxChars)
            {
                return trimmed;
            }

            var sb = new System.Text.StringBuilder();
            int wordCount = 0;
            foreach (var word in words)
            {
                if (wordCount >= maxWords) break;
                if (sb.Length > 0 && (sb.Length + 1 + word.Length) > maxChars) break;

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(word);
                wordCount++;
            }

            if (sb.Length == 0)
            {
                string first = words[0];
                return first.Length > maxChars ? first.Substring(0, Math.Max(1, maxChars - 4)).TrimEnd() + " ..." : first;
            }

            string result = sb.ToString().TrimEnd(' ', '&', '-', ',', ':', ';', '.');
            return result + " ...";
        }

        public string FullPath => _note.FullPath;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => Title;
    }
}
