using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using Xunit;

namespace QuickNote.Tests.ViewModels
{
    public class MobileSelectionViewModelTests
    {
        [Fact]
        public void FolderSelection_EnterToggleAndExit_UpdatesStateCorrectly()
        {
            var vm = new MobileSelectionViewModel();
            Assert.False(vm.IsFolderSelectionMode);
            Assert.Equal(0, vm.SelectedFolderCount);

            vm.EnterFolderSelection("FolderA");
            Assert.True(vm.IsFolderSelectionMode);
            Assert.Equal(1, vm.SelectedFolderCount);
            Assert.True(vm.IsFolderSelected("FolderA"));
            Assert.Equal("1 selected", vm.FolderSelectionCountText);

            vm.ToggleFolder("FolderB");
            Assert.Equal(2, vm.SelectedFolderCount);
            Assert.True(vm.IsFolderSelected("FolderB"));

            vm.ToggleFolder("FolderA");
            Assert.Equal(1, vm.SelectedFolderCount);
            Assert.False(vm.IsFolderSelected("FolderA"));

            vm.ExitFolderSelection();
            Assert.False(vm.IsFolderSelectionMode);
            Assert.Equal(0, vm.SelectedFolderCount);
        }

        [Fact]
        public void NoteSelection_ToggleSelectAll_SelectsAndDeselectsAll()
        {
            var vm = new MobileSelectionViewModel();
            var allNotes = new List<string> { "note1.md", "note2.md", "note3.md" };

            vm.EnterNoteSelection("note1.md");
            Assert.True(vm.IsNoteSelectionMode);
            Assert.Equal(1, vm.SelectedNoteCount);

            // Select all
            vm.ToggleSelectAllNotes(allNotes);
            Assert.Equal(3, vm.SelectedNoteCount);
            Assert.True(vm.IsNoteSelected("note1.md"));
            Assert.True(vm.IsNoteSelected("note2.md"));
            Assert.True(vm.IsNoteSelected("note3.md"));

            // Toggle select all again when all selected -> should clear
            vm.ToggleSelectAllNotes(allNotes);
            Assert.Equal(0, vm.SelectedNoteCount);
        }
    }
}
