using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ReplaceBackgroundInterpolationTests
    {
        [Fact]
        public void DetermineSourceEdges_SelectionTouchingLeftEdge_ExcludesLeftOnly()
        {
            var edges = ReplaceBackgroundInterpolation.DetermineSourceEdges(
                new Rectangle(0, 10, 20, 30),
                new Size(100, 100));

            Assert.False(edges.UseLeft);
            Assert.True(edges.UseTop);
            Assert.True(edges.UseRight);
            Assert.True(edges.UseBottom);
            Assert.True(edges.HasAnySource);
        }

        [Fact]
        public void DetermineSourceEdges_SelectionTouchingTopAndRight_ExcludesBoth()
        {
            var edges = ReplaceBackgroundInterpolation.DetermineSourceEdges(
                new Rectangle(40, 0, 60, 20),
                new Size(100, 100));

            Assert.True(edges.UseLeft);
            Assert.False(edges.UseTop);
            Assert.False(edges.UseRight);
            Assert.True(edges.UseBottom);
            Assert.True(edges.HasAnySource);
        }

        [Fact]
        public void DetermineSourceEdges_SelectionCoveringEntireImage_HasNoSourceEdges()
        {
            var edges = ReplaceBackgroundInterpolation.DetermineSourceEdges(
                new Rectangle(0, 0, 100, 100),
                new Size(100, 100));

            Assert.False(edges.UseLeft);
            Assert.False(edges.UseTop);
            Assert.False(edges.UseRight);
            Assert.False(edges.UseBottom);
            Assert.False(edges.HasAnySource);
        }
    }
}