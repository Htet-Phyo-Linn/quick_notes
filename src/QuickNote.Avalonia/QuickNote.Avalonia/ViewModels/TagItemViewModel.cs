using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickNote.Avalonia.ViewModels
{
    public partial class TagItemViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isEditing;
        [ObservableProperty] private string _editText = string.Empty;

        public TagItemViewModel(string name)
        {
            Name = name;
            EditText = name;
        }
    }
}
