using QuickNote.Core.Outline;
using Xunit;

namespace QuickNote.Tests.Outline
{
    public class OutlineBuilderTests
    {
        private readonly OutlineBuilder _builder = new OutlineBuilder();

        [Fact]
        public void BuildOutline_BuildsHierarchicalHeadingTree()
        {
            var markdown = @"# Root H1
Text here.
## Sub H2
Subtext.
### Nested H3
Nested text.
## Another H2
Another text.";

            var tree = _builder.BuildOutline(markdown);

            Assert.Single(tree); // 1 root H1
            var root = tree[0];
            Assert.Equal("Root H1", root.Text);
            Assert.Equal(1, root.Level);
            Assert.Equal(2, root.Children.Count); // Two H2s

            var firstH2 = root.Children[0];
            Assert.Equal("Sub H2", firstH2.Text);
            Assert.Single(firstH2.Children); // One H3

            var nestedH3 = firstH2.Children[0];
            Assert.Equal("Nested H3", nestedH3.Text);
            Assert.Equal(3, nestedH3.Level);

            var secondH2 = root.Children[1];
            Assert.Equal("Another H2", secondH2.Text);
        }

        [Fact]
        public void FindActiveHeading_ReturnsNearestPrecedingHeading()
        {
            var markdown = @"# Introduction
Line 2
Line 3
## Details
Line 5
Line 6";

            var tree = _builder.BuildOutline(markdown);

            var headingAtLine3 = _builder.FindActiveHeading(tree, 3);
            Assert.NotNull(headingAtLine3);
            Assert.Equal("Introduction", headingAtLine3!.Text);

            var headingAtLine5 = _builder.FindActiveHeading(tree, 5);
            Assert.NotNull(headingAtLine5);
            Assert.Equal("Details", headingAtLine5!.Text);
        }

        [Fact]
        public void BuildOutline_IgnoresHeadingsInsideCodeBlocksAndFrontmatter()
        {
            var markdown = @"---
title: My Title
# Comment in frontmatter
---

# Real Heading 1

Some text.

```powershell
# 1. System Identity, IP & Applets Status
function Check-SystemIdentity {
    # Another comment
    # ====================
}
```

## Real Heading 2

```python
# python comment
def foo():
    pass
```
";
            var tree = _builder.BuildOutline(markdown);
            Assert.Single(tree);
            Assert.Equal("Real Heading 1", tree[0].Text);
            Assert.Single(tree[0].Children);
            Assert.Equal("Real Heading 2", tree[0].Children[0].Text);
        }
    }
}
