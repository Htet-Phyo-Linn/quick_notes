using QuickNote.App.ViewModels;
using QuickNote.Core.Models;
using Xunit;

namespace QuickNote.Tests.ViewModels
{
    public class NoteTabViewModelTests
    {
        [Fact]
        public void FormatWebTitle_ShortTitle_ReturnsOriginal()
        {
            string result = NoteTabViewModel.FormatWebTitle("Broker VM Req");
            Assert.Equal("Broker VM Req", result);
        }

        [Fact]
        public void FormatWebTitle_EmptyOrNull_ReturnsUntitled()
        {
            Assert.Equal("Untitled", NoteTabViewModel.FormatWebTitle(""));
            Assert.Equal("Untitled", NoteTabViewModel.FormatWebTitle("   "));
            Assert.Equal("Untitled", NoteTabViewModel.FormatWebTitle(null!));
        }

        [Fact]
        public void FormatWebTitle_ExceedsWordLimit_TruncatesAtWordBoundaryWithEllipsis()
        {
            string result = NoteTabViewModel.FormatWebTitle("Cortex XDR Broker VM Connectivity");
            Assert.Equal("Cortex XDR Broker VM ...", result);
        }

        [Fact]
        public void FormatWebTitle_LongWords_ProducesUserRequestedStyle()
        {
            string result = NoteTabViewModel.FormatWebTitle("broker connectivity configuration and setup guide");
            Assert.Equal("broker connectivity ...", result);
        }

        [Fact]
        public void FormatWebTitle_TrailingPunctuation_CleansUpBeforeEllipsis()
        {
            string result = NoteTabViewModel.FormatWebTitle("Broker VM Setup & Requirements In Detail");
            Assert.Equal("Broker VM Setup ...", result);
        }

        [Fact]
        public void NoteTabViewModel_TitleChange_UpdatesDisplayTitleAndRaisesEvents()
        {
            var note = new NoteDocument
            {
                Title = "Original Title",
                FileName = "original.md",
                FullPath = "C:\\notes\\original.md"
            };

            var vm = new NoteTabViewModel(note);
            Assert.Equal("Original Title", vm.Title);
            Assert.Equal("Original Title", vm.DisplayTitle);

            bool titleChanged = false;
            bool displayTitleChanged = false;

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NoteTabViewModel.Title)) titleChanged = true;
                if (e.PropertyName == nameof(NoteTabViewModel.DisplayTitle)) displayTitleChanged = true;
            };

            vm.Title = "Cortex XDR Broker VM Connectivity";

            Assert.True(titleChanged);
            Assert.True(displayTitleChanged);
            Assert.Equal("Cortex XDR Broker VM Connectivity", vm.Title);
            Assert.Equal("Cortex XDR Broker VM ...", vm.DisplayTitle);
        }

        [Fact]
        public void IsEmptyUntitledNote_BlankNote_ReturnsTrue()
        {
            var note = new NoteDocument
            {
                Title = "",
                FileName = "Untitled.md",
                Content = "",
                Tags = System.Array.Empty<string>()
            };

            Assert.True(MainViewModel.IsEmptyUntitledNote(note));
        }

        [Fact]
        public void IsEmptyUntitledNote_NoteWithContent_ReturnsFalse()
        {
            var note = new NoteDocument
            {
                Title = "Untitled",
                FileName = "Untitled (1).md",
                Content = "# My Note Content",
                Tags = System.Array.Empty<string>()
            };

            Assert.False(MainViewModel.IsEmptyUntitledNote(note));
        }

        [Fact]
        public void IsEmptyUntitledNote_NoteWithTitle_ReturnsFalse()
        {
            var note = new NoteDocument
            {
                Title = "Docker Installation",
                FileName = "docker.md",
                Content = "",
                Tags = System.Array.Empty<string>()
            };

            Assert.False(MainViewModel.IsEmptyUntitledNote(note));
        }
    }
}
