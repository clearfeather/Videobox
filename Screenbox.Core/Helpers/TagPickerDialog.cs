#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Screenbox.Core.Helpers;

public static class TagPickerDialog
{
    private const string NoTagOption = "No tag";

    public static async Task<string?> ShowAsync(
        string title,
        IReadOnlyList<string> availableTags,
        string? currentTag = null,
        string primaryButtonText = "Save")
    {
        string[] tagOptions = availableTags
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ComboBox existingTagBox = new()
        {
            Header = "Existing tag",
            MinWidth = 300
        };

        existingTagBox.Items.Add(NoTagOption);
        foreach (string tagOption in tagOptions)
        {
            existingTagBox.Items.Add(tagOption);
        }

        string selectedTag = tagOptions.FirstOrDefault(name =>
            name.Equals(currentTag, StringComparison.CurrentCultureIgnoreCase)) ?? NoTagOption;
        existingTagBox.SelectedItem = selectedTag;

        TextBox newTagBox = new()
        {
            Header = "New tag",
            PlaceholderText = "Type a new tag name",
            MinWidth = 300,
            Margin = new Thickness(0, 12, 0, 0)
        };

        newTagBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(newTagBox.Text))
            {
                existingTagBox.SelectedItem = NoTagOption;
            }
        };

        existingTagBox.SelectionChanged += (_, _) =>
        {
            if (existingTagBox.SelectedItem is string selectedExistingTag &&
                !selectedExistingTag.Equals(NoTagOption, StringComparison.CurrentCultureIgnoreCase))
            {
                newTagBox.Text = string.Empty;
            }
        };

        StackPanel content = new()
        {
            Children =
            {
                existingTagBox,
                newTagBox
            }
        };

        ContentDialog dialog = new()
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        string newTag = newTagBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(newTag))
        {
            return newTag;
        }

        return existingTagBox.SelectedItem is string tag &&
               !tag.Equals(NoTagOption, StringComparison.CurrentCultureIgnoreCase)
            ? tag
            : string.Empty;
    }
}
