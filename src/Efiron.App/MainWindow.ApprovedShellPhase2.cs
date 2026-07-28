using Efiron.App.Playlists;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private bool _approvedShellInitializationScheduled;
    private bool _approvedShellInitialized;
    private bool _isSynchronizingChannelsWorkspace;
    private bool _isSynchronizingSettingsNavigation;

    private ResourceLoader _approvedShellResources = null!;
    private NavigationViewItem _channelsNavigationItem = null!;
    private Grid _channelsView = null!;
    private ListView _channelsManagementListView = null!;
    private TextBox _channelsManagementSearchTextBox = null!;
    private ComboBox _channelsManagementCategoryComboBox = null!;
    private TextBlock _channelsManagementSummaryText = null!;
    private NavigationView _settingsNavigation = null!;
    private Grid _settingsContentGrid = null!;
    private StackPanel _settingsGeneralPanel = null!;
    private StackPanel _settingsSourcesPanel = null!;
    private StackPanel _settingsInterfacePanel = null!;
    private StackPanel _settingsPlayerPanel = null!;
    private StackPanel _settingsRemotePanel = null!;
    private StackPanel _settingsDataPanel = null!;
    private StackPanel _settingsAboutPanel = null!;

    internal void ScheduleApprovedShellPhase2Initialization()
    {
        if (_approvedShellInitializationScheduled || _approvedShellInitialized)
        {
            return;
        }

        _approvedShellInitializationScheduled = true;
        RootNavigation.Loaded += ApprovedShellRootNavigation_Loaded;
        Closed += ApprovedShellStartupWindow_Closed;
    }

    private void ApprovedShellRootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Loaded -= ApprovedShellRootNavigation_Loaded;
        Closed -= ApprovedShellStartupWindow_Closed;
        _approvedShellInitializationScheduled = false;
        InitializeApprovedShellPhase2();
    }

    private void ApprovedShellStartupWindow_Closed(object sender, WindowEventArgs args)
    {
        RootNavigation.Loaded -= ApprovedShellRootNavigation_Loaded;
        Closed -= ApprovedShellStartupWindow_Closed;
        _approvedShellInitializationScheduled = false;
    }

    private void InitializeApprovedShellPhase2()
    {
        if (_approvedShellInitialized)
        {
            return;
        }

        _approvedShellInitialized = true;
        _approvedShellResources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "ApprovedShell");

        ConfigureApprovedRootShell();
        CreateApprovedChannelsWorkspace();
        CreateApprovedSettingsWorkspace();
        MoveTechnicalControlsToApprovedSections();

        RootNavigation.SelectionChanged += ApprovedShellRootNavigation_SelectionChanged;
        Closed += ApprovedShellWindow_Closed;

        RefreshApprovedChannelsWorkspace(
            ChannelListView.ItemsSource?.OfType<ChannelListItem>().ToList() ?? [],
            (ChannelListView.SelectedItem as ChannelListItem)?.Channel.StableId);
    }

    private void ConfigureApprovedRootShell()
    {
        RootNavigation.OpenPaneLength = 220;
        RootNavigation.CompactPaneLength = 48;
        RootNavigation.IsPaneOpen = true;
        RootNavigation.AlwaysShowHeader = false;
        RootNavigation.Header = null;
        StatusNavigationItem.Visibility = Visibility.Collapsed;

        _channelsNavigationItem = new NavigationViewItem
        {
            Content = _approvedShellResources.GetString("NavigationChannels"),
            Tag = "channels",
            Icon = new FontIcon { Glyph = "\uE8B7" },
        };
        ToolTipService.SetToolTip(
            _channelsNavigationItem,
            _approvedShellResources.GetString("NavigationChannels"));
        RootNavigation.MenuItems.Add(_channelsNavigationItem);
    }

    private void CreateApprovedChannelsWorkspace()
    {
        _channelsView = new Grid
        {
            RowSpacing = 12,
            Visibility = Visibility.Collapsed,
        };
        _channelsView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _channelsView.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(new TextBlock
        {
            Text = _approvedShellResources.GetString("ChannelsTitle"),
            Style = (Style)Application.Current.Resources["EfironPageTitleTextStyle"],
        });
        header.Children.Add(new TextBlock
        {
            Text = _approvedShellResources.GetString("ChannelsDescription"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
        });
        _channelsView.Children.Add(header);

        var workspace = new Grid { ColumnSpacing = 12 };
        workspace.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(3, GridUnitType.Star),
        });
        workspace.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(2, GridUnitType.Star),
        });
        Grid.SetRow(workspace, 1);
        _channelsView.Children.Add(workspace);

        var listPanel = new Grid { RowSpacing = 8 };
        listPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listPanel.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        listPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var filterGrid = new Grid { ColumnSpacing = 8 };
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(3, GridUnitType.Star),
        });
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(2, GridUnitType.Star),
        });

        _channelsManagementSearchTextBox = new TextBox
        {
            PlaceholderText = _approvedShellResources.GetString("ChannelsSearchPlaceholder"),
        };
        _channelsManagementSearchTextBox.TextChanged +=
            ChannelsManagementSearchTextBox_TextChanged;
        filterGrid.Children.Add(_channelsManagementSearchTextBox);

        _channelsManagementCategoryComboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = _approvedShellResources.GetString("ChannelsCategoryPlaceholder"),
        };
        _channelsManagementCategoryComboBox.SelectionChanged +=
            ChannelsManagementCategoryComboBox_SelectionChanged;
        Grid.SetColumn(_channelsManagementCategoryComboBox, 1);
        filterGrid.Children.Add(_channelsManagementCategoryComboBox);
        listPanel.Children.Add(filterGrid);

        _channelsManagementSummaryText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
        };
        Grid.SetRow(_channelsManagementSummaryText, 1);
        listPanel.Children.Add(_channelsManagementSummaryText);

        _channelsManagementListView = new ListView
        {
            ItemTemplate = ChannelListView.ItemTemplate,
            SelectionMode = ListViewSelectionMode.Single,
        };
        _channelsManagementListView.SelectionChanged +=
            ChannelsManagementListView_SelectionChanged;
        Grid.SetRow(_channelsManagementListView, 2);
        listPanel.Children.Add(_channelsManagementListView);

        var listSurface = new Border
        {
            Style = (Style)Application.Current.Resources["EfironSurfaceBorderStyle"],
            Child = listPanel,
        };
        workspace.Children.Add(listSurface);

        var managementPanel = new StackPanel
        {
            Spacing = 12,
            Tag = "approved-channel-management-panel",
        };
        managementPanel.Children.Add(new TextBlock
        {
            Text = _approvedShellResources.GetString("ChannelsManagementTitle"),
            Style = (Style)Application.Current.Resources["EfironSectionTitleTextStyle"],
        });
        managementPanel.Children.Add(new TextBlock
        {
            Text = _approvedShellResources.GetString("ChannelsManagementDescription"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
        });

        var managementSurface = new Border
        {
            Grid.Column = 1,
            Style = (Style)Application.Current.Resources["EfironSurfaceBorderStyle"],
            Child = managementPanel,
        };
        Grid.SetColumn(managementSurface, 1);
        workspace.Children.Add(managementSurface);

        ContentRoot.Children.Add(_channelsView);
    }

    private void CreateApprovedSettingsWorkspace()
    {
        var existingChildren = SettingsView.Children.ToList();
        SettingsView.Children.Clear();
        SettingsView.Spacing = 0;

        _settingsContentGrid = new Grid();
        _settingsGeneralPanel = CreateSettingsPanel("SettingsGeneral", "SettingsGeneralDescription");
        _settingsSourcesPanel = CreateSettingsPanel("SettingsSources", "SettingsSourcesDescription");
        _settingsInterfacePanel = CreateSettingsPanel("SettingsInterface", "SettingsInterfaceDescription");
        _settingsPlayerPanel = CreateSettingsPanel("SettingsPlayer", "SettingsPlayerDescription");
        _settingsRemotePanel = CreateSettingsPanel("SettingsRemote", "SettingsRemoteDescription");
        _settingsDataPanel = CreateSettingsPanel("SettingsData", "SettingsDataDescription");
        _settingsAboutPanel = CreateSettingsPanel("SettingsAbout", "SettingsAboutDescription");

        foreach (var panel in new[]
                 {
                     _settingsGeneralPanel,
                     _settingsSourcesPanel,
                     _settingsInterfacePanel,
                     _settingsPlayerPanel,
                     _settingsRemotePanel,
                     _settingsDataPanel,
                     _settingsAboutPanel,
                 })
        {
            panel.Visibility = Visibility.Collapsed;
            _settingsContentGrid.Children.Add(new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
        }

        var appearanceCard = existingChildren.OfType<Border>().FirstOrDefault();
        foreach (var child in existingChildren)
        {
            if (ReferenceEquals(child, appearanceCard))
            {
                _settingsInterfacePanel.Children.Add(child);
            }
            else
            {
                _settingsGeneralPanel.Children.Add(child);
            }
        }

        _settingsNavigation = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsPaneOpen = true,
            OpenPaneLength = 190,
            CompactPaneLength = 48,
            AlwaysShowHeader = false,
            Content = _settingsContentGrid,
            MinHeight = 520,
        };
        AddSettingsNavigationItem("general", "SettingsGeneral", "\uE713");
        AddSettingsNavigationItem("sources", "SettingsSources", "\uE774");
        AddSettingsNavigationItem("interface", "SettingsInterface", "\uE771");
        AddSettingsNavigationItem("player", "SettingsPlayer", "\uE714");
        AddSettingsNavigationItem("remote", "SettingsRemote", "\uE7F4");
        AddSettingsNavigationItem("data", "SettingsData", "\uE8F1");
        AddSettingsNavigationItem("about", "SettingsAbout", "\uE946");
        _settingsNavigation.SelectionChanged += SettingsNavigation_SelectionChanged;

        SettingsView.Children.Add(_settingsNavigation);
        _settingsNavigation.SelectedItem = _settingsNavigation.MenuItems[0];
        ShowSettingsPanel("general");
    }

    private StackPanel CreateSettingsPanel(string titleKey, string descriptionKey)
    {
        var panel = new StackPanel
        {
            Spacing = 16,
            Padding = new Thickness(8, 4, 8, 16),
        };
        panel.Children.Add(new TextBlock
        {
            Text = _approvedShellResources.GetString(titleKey),
            Style = (Style)Application.Current.Resources["EfironPageTitleTextStyle"],
        });
        panel.Children.Add(new TextBlock
        {
            Text = _approvedShellResources.GetString(descriptionKey),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
        });
        return panel;
    }

    private void AddSettingsNavigationItem(string tag, string resourceKey, string glyph)
    {
        _settingsNavigation.MenuItems.Add(new NavigationViewItem
        {
            Content = _approvedShellResources.GetString(resourceKey),
            Tag = tag,
            Icon = new FontIcon { Glyph = glyph },
        });
    }

    private void MoveTechnicalControlsToApprovedSections()
    {
        MovePlaylistSourceToSettings();
        MoveEpgSourceToSettings();
        MoveDirectStreamToPlayerSettings();
        MoveChannelLibraryControlsToChannels();
    }

    private void MovePlaylistSourceToSettings()
    {
        if (PlaylistSourceTextBox.Parent is not Grid sourceGrid ||
            sourceGrid.Parent is not StackPanel sourceSection ||
            sourceSection.Parent is not Grid liveSidebarGrid)
        {
            throw new InvalidOperationException("Playlist source section was not found.");
        }

        liveSidebarGrid.Children.Remove(sourceSection);
        _settingsSourcesPanel.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["EfironSurfaceBorderStyle"],
            Child = sourceSection,
        });
    }

    private void MoveEpgSourceToSettings()
    {
        if (EpgSourceTextBox.Parent is not Grid sourceGrid ||
            sourceGrid.Parent is not StackPanel sourceSection ||
            sourceSection.Parent is not Border sourceBorder ||
            sourceBorder.Parent is not Grid guideGrid)
        {
            throw new InvalidOperationException("EPG source section was not found.");
        }

        guideGrid.Children.Remove(sourceBorder);
        Grid.SetRow(sourceBorder, 0);
        _settingsSourcesPanel.Children.Add(sourceBorder);
    }

    private void MoveDirectStreamToPlayerSettings()
    {
        if (DirectStreamExpander.Parent is Panel parent)
        {
            parent.Children.Remove(DirectStreamExpander);
        }

        DirectStreamExpander.Margin = new Thickness(0);
        _settingsPlayerPanel.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["EfironSurfaceBorderStyle"],
            Child = DirectStreamExpander,
        });
    }

    private void MoveChannelLibraryControlsToChannels()
    {
        if (_channelNumberingComboBox.Parent is not Grid toolbar ||
            toolbar.Parent is not StackPanel libraryControls ||
            libraryControls.Parent is not StackPanel filterPanel)
        {
            throw new InvalidOperationException("Channel library controls were not found.");
        }

        filterPanel.Children.Remove(libraryControls);
        var managementPanel = FindTaggedStackPanel(
            _channelsView,
            "approved-channel-management-panel") ??
            throw new InvalidOperationException("Channel management panel was not found.");
        managementPanel.Children.Add(libraryControls);
    }

    private static StackPanel? FindTaggedStackPanel(DependencyObject root, string tag)
    {
        if (root is StackPanel panel && string.Equals(panel.Tag as string, tag, StringComparison.Ordinal))
        {
            return panel;
        }

        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
            var result = FindTaggedStackPanel(child, tag);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private void ApprovedShellRootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag as string;
        var showChannels = string.Equals(tag, "channels", StringComparison.Ordinal);
        _channelsView.Visibility = showChannels ? Visibility.Visible : Visibility.Collapsed;

        if (!showChannels)
        {
            return;
        }

        LiveView.Visibility = Visibility.Collapsed;
        GuideView.Visibility = Visibility.Collapsed;
        ArchiveView.Visibility = Visibility.Collapsed;
        RecordingsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        RefreshApprovedChannelsWorkspace(
            ChannelListView.ItemsSource?.OfType<ChannelListItem>().ToList() ?? [],
            (ChannelListView.SelectedItem as ChannelListItem)?.Channel.StableId);
    }

    private void SettingsNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSynchronizingSettingsNavigation || args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        ShowSettingsPanel(tag);
    }

    private void ShowSettingsPanel(string tag)
    {
        _settingsGeneralPanel.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        _settingsSourcesPanel.Visibility = tag == "sources" ? Visibility.Visible : Visibility.Collapsed;
        _settingsInterfacePanel.Visibility = tag == "interface" ? Visibility.Visible : Visibility.Collapsed;
        _settingsPlayerPanel.Visibility = tag == "player" ? Visibility.Visible : Visibility.Collapsed;
        _settingsRemotePanel.Visibility = tag == "remote" ? Visibility.Visible : Visibility.Collapsed;
        _settingsDataPanel.Visibility = tag == "data" ? Visibility.Visible : Visibility.Collapsed;
        _settingsAboutPanel.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChannelsManagementSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSynchronizingChannelsWorkspace)
        {
            return;
        }

        ChannelSearchTextBox.Text = _channelsManagementSearchTextBox.Text;
    }

    private void ChannelsManagementCategoryComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizingChannelsWorkspace)
        {
            return;
        }

        GroupFilterComboBox.SelectedIndex = _channelsManagementCategoryComboBox.SelectedIndex;
    }

    private void ChannelsManagementListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingChannelsWorkspace)
        {
            return;
        }

        _isSynchronizingChannelsWorkspace = true;
        ChannelListView.SelectedItem = _channelsManagementListView.SelectedItem;
        _isSynchronizingChannelsWorkspace = false;
    }

    internal void RefreshApprovedChannelsWorkspace(
        IReadOnlyList<ChannelListItem> visibleItems,
        string? preferredStableId)
    {
        if (!_approvedShellInitialized)
        {
            return;
        }

        _isSynchronizingChannelsWorkspace = true;
        try
        {
            _channelsManagementSearchTextBox.Text = ChannelSearchTextBox.Text;
            RefreshApprovedChannelCategoryFilter();
            _channelsManagementListView.ItemsSource = visibleItems;
            var selectedIndex = visibleItems
                .Select((item, index) => (item, index))
                .FirstOrDefault(pair => string.Equals(
                    pair.item.Channel.StableId,
                    preferredStableId,
                    StringComparison.Ordinal))
                .index;
            _channelsManagementListView.SelectedIndex = visibleItems.Count == 0
                ? -1
                : Math.Clamp(selectedIndex, 0, visibleItems.Count - 1);
            _channelsManagementSummaryText.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _approvedShellResources.GetString("ChannelsSummaryFormat"),
                visibleItems.Count,
                _channelCatalog.Count);
        }
        finally
        {
            _isSynchronizingChannelsWorkspace = false;
        }
    }

    private void RefreshApprovedChannelCategoryFilter()
    {
        var selectedIndex = GroupFilterComboBox.SelectedIndex;
        _channelsManagementCategoryComboBox.Items.Clear();
        foreach (var item in GroupFilterComboBox.Items.OfType<ComboBoxItem>())
        {
            _channelsManagementCategoryComboBox.Items.Add(new ComboBoxItem
            {
                Content = item.Content,
                Tag = item.Tag,
            });
        }

        _channelsManagementCategoryComboBox.SelectedIndex =
            _channelsManagementCategoryComboBox.Items.Count == 0
                ? -1
                : Math.Clamp(selectedIndex, 0, _channelsManagementCategoryComboBox.Items.Count - 1);
    }

    private void ApprovedShellWindow_Closed(object sender, WindowEventArgs args)
    {
        RootNavigation.SelectionChanged -= ApprovedShellRootNavigation_SelectionChanged;
        _settingsNavigation.SelectionChanged -= SettingsNavigation_SelectionChanged;
        _channelsManagementSearchTextBox.TextChanged -=
            ChannelsManagementSearchTextBox_TextChanged;
        _channelsManagementCategoryComboBox.SelectionChanged -=
            ChannelsManagementCategoryComboBox_SelectionChanged;
        _channelsManagementListView.SelectionChanged -=
            ChannelsManagementListView_SelectionChanged;
        Closed -= ApprovedShellWindow_Closed;
    }
}
