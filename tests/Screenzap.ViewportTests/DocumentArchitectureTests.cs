using System;
using System.Reflection;
using System.Windows.Forms;
using screenzap;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

public class DocumentArchitectureTests
{
    [Theory]
    [InlineData(typeof(ImageDocumentEditor))]
    [InlineData(typeof(ClipboardDocumentHost))]
    [InlineData(typeof(ImageViewport))]
    public void DocumentComponentsCannotCreateAHiddenEditorPresentation(Type type)
    {
        Assert.False(typeof(Control).IsAssignableFrom(type));
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Assert.False(typeof(Control).IsAssignableFrom(field.FieldType), $"{type.Name}.{field.Name} owns a UI control");
    }
}
