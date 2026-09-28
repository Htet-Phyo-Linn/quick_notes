using System.Collections.Generic;
using QuickNote.Core.Models;

namespace QuickNote.Core.Interfaces
{
    public interface IOutlineService
    {
        List<Heading> BuildOutline(string markdownContent);
        Heading? FindActiveHeading(IEnumerable<Heading> outline, int cursorLineNumber);
    }
}
