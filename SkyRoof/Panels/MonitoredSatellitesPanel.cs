using System.Drawing.Drawing2D;
using Serilog;
using WeifenLuo.WinFormsUI.Docking;
using VE3NEA;

namespace SkyRoof
{
  public class MonitoredSatellitesPanel : DockContent
  {
    private readonly Context ctx;
    private readonly ComboBox presetComboBox = new();
    private readonly Button newListBtn = new();
    private readonly Button renameListBtn = new();
    private readonly Button copyListBtn = new();
    private readonly ListView listView = new();
    private readonly Button upBtn = new();
    private readonly Button downBtn = new();
    private readonly Button removeBtn = new();
    private readonly Label minElLabel = new();
    private readonly TrackBar minElTrackBar = new();
    private readonly Label minElValueLabel = new();
    private readonly ToolTip minElToolTip = new();
    private readonly ToolTip listColumnToolTip = new();
    private readonly MiniSkyPopup skyMapPopup = new();
    private readonly TransmitterTipPopup transmitterPopup = new();
    private readonly System.Windows.Forms.Timer nextPassTimer = new();
    private readonly System.Windows.Forms.Timer minElSaveTimer = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer skyMapTimer = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer transmitterTimer = new() { Interval = 400 };
    private ListViewItem? skyHoverItem;
    private SatellitePass? skyHoverPass;
    private ListViewItem? transmitterHoverItem;
    private SatnogsDbTransmitter? transmitterHoverTx;
    private int? DragSourceIndex;
    private bool refreshingList;
    private bool refreshingPresets;

    private const string MinElevationToolTip =
      "The minimum elevation required for a higher priority satellite to interrupt a lower priority one during a pass.";
    private const string MaxPassToolTip = "Max elevation of the next pass";
    private const string NextPassToolTip = "Time until the next pass";
    private const string AudioRecordToolTip = "Enable audio recording for this satellite";
    private const string IqRecordToolTip = "Enable I/Q recording for this satellite.";
    private const string RotatorToolTip =
      "Automatically track this satellite with the rotator starting 30 seconds before the pass";
    private const int SatelliteColumnIndex = 1;
    private const int SatelliteColumnMaxWidth = 240;
    private const int TransmitterColumnIndex = 2;
    private const int NextColumnIndex = 3;
    private const int MaxColumnIndex = 4;
    private const int AudioColumnIndex = 5;
    private const int IqColumnIndex = 6;
    private const int RotatorColumnIndex = 7;
    private static readonly Color SelectedSatBackColor = Color.LightGreen;
    private static readonly Color NextMonitoredBackColor = Color.LightGoldenrodYellow;

    public MonitoredSatellitesPanel(Context ctx)
    {
      Log.Information("Creating MonitoredSatellitesPanel");
      this.ctx = ctx;
      Text = "Monitored Satellites";

      ctx.MonitoredSatellitesPanel = this;
      ctx.MainForm.MonitoredSatellitesMNU.Checked = true;

      BuildUi();
      RefreshList();

      nextPassTimer.Interval = 1000;
      nextPassTimer.Tick += (s, e) => UpdateNextPassCountdown();
      nextPassTimer.Start();

      minElSaveTimer.Tick += (s, e) =>
      {
        minElSaveTimer.Stop();
        ctx.Settings.SaveToFile();
      };

      skyMapTimer.Tick += SkyMapTimer_Tick;
      transmitterTimer.Tick += TransmitterTimer_Tick;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      Log.Information("Closing MonitoredSatellitesPanel");
      nextPassTimer.Stop();
      nextPassTimer.Dispose();
      minElSaveTimer.Stop();
      minElSaveTimer.Dispose();
      skyMapTimer.Stop();
      skyMapTimer.Dispose();
      transmitterTimer.Stop();
      transmitterTimer.Dispose();
      HideSkyMapPopup();
      HideTransmitterPopup();
      skyMapPopup.Dispose();
      transmitterPopup.Dispose();
      minElToolTip.Dispose();
      listColumnToolTip.Dispose();
      ctx.Settings.Ui.SaveColumnWidths("MonitoredSatellitesPanel", listView);
      ctx.MonitoredSatellitesPanel = null;
      ctx.MainForm.MonitoredSatellitesMNU.Checked = false;
      base.OnFormClosing(e);
    }

    private void BuildUi()
    {
      var presetBar = new FlowLayoutPanel
      {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8, 6, 8, 6),
      };

      var presetButtons = new FlowLayoutPanel
      {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0),
      };

      ConfigureGlyphButton(newListBtn, "\uE710", "New list", minElToolTip);
      newListBtn.Click += NewListBtn_Click;
      presetButtons.Controls.Add(newListBtn);

      ConfigureGlyphButton(renameListBtn, "\uE8AC", "Rename list", minElToolTip);
      renameListBtn.Click += RenameListBtn_Click;
      presetButtons.Controls.Add(renameListBtn);

      ConfigureGlyphButton(copyListBtn, "\uE8C8", "Copy list", minElToolTip);
      copyListBtn.Click += CopyListBtn_Click;
      presetButtons.Controls.Add(copyListBtn);

      presetComboBox.Width = 160;
      presetComboBox.Height = 28;
      presetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
      presetComboBox.FormattingEnabled = true;
      presetComboBox.DisplayMember = "Name";
      presetComboBox.Margin = new Padding(0, 0, 6, 0);
      presetComboBox.SelectedIndexChanged += PresetComboBox_SelectedIndexChanged;

      presetBar.Controls.Add(presetComboBox);
      presetBar.Controls.Add(presetButtons);

      var top = new FlowLayoutPanel
      {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8, 6, 8, 6),
      };

      ConfigureIconButton(upBtn, RotateBitmap(Properties.Resources.arrow_down_16, RotateFlipType.Rotate180FlipNone), "Move up");
      upBtn.Click += (s, e) => MoveSelected(-1);

      ConfigureIconButton(downBtn, Properties.Resources.arrow_down_16, "Move down");
      downBtn.Click += (s, e) => MoveSelected(+1);

      ConfigureGlyphButton(removeBtn, "\uE74D", "Remove");
      removeBtn.Click += (s, e) => RemoveSelected();

      top.Controls.Add(upBtn);
      top.Controls.Add(downBtn);
      top.Controls.Add(removeBtn);

      minElLabel.Text = "Min Elevation:";
      minElLabel.AutoSize = true;
      minElLabel.Margin = new Padding(8, 6, 4, 0);
      top.Controls.Add(minElLabel);

      minElTrackBar.Minimum = 0;
      minElTrackBar.Maximum = 90;
      minElTrackBar.TickFrequency = 10;
      minElTrackBar.SmallChange = 1;
      minElTrackBar.LargeChange = 5;
      minElTrackBar.AutoSize = false;
      minElTrackBar.Height = 24;
      minElTrackBar.Width = 120;
      minElTrackBar.Margin = new Padding(0, 2, 4, 0);
      minElTrackBar.Value = Math.Max(0, Math.Min(90, ctx.Settings.Satellites.AutoMonitorMinElevationDeg));
      minElValueLabel.Text = $"{minElTrackBar.Value}°";
      minElTrackBar.ValueChanged += (s, e) =>
      {
        ctx.Settings.Satellites.AutoMonitorMinElevationDeg = minElTrackBar.Value;
        minElValueLabel.Text = $"{minElTrackBar.Value}°";
        minElSaveTimer.Stop();
        minElSaveTimer.Start();
        UpdateRowHighlights();
      };
      minElTrackBar.MouseUp += (s, e) =>
      {
        minElSaveTimer.Stop();
        ctx.Settings.SaveToFile();
      };
      top.Controls.Add(minElTrackBar);

      minElValueLabel.AutoSize = true;
      minElValueLabel.Margin = new Padding(0, 6, 0, 0);
      top.Controls.Add(minElValueLabel);

      minElToolTip.SetToolTip(minElLabel, MinElevationToolTip);
      minElToolTip.SetToolTip(minElTrackBar, MinElevationToolTip);
      minElToolTip.SetToolTip(minElValueLabel, MinElevationToolTip);

      listView.Dock = DockStyle.Fill;
      listView.View = View.Details;
      listView.FullRowSelect = true;
      listView.MultiSelect = false;
      listView.AllowDrop = true;
      EnableDoubleBuffering(listView);
      listView.Columns.Add("#", 40);
      listView.Columns.Add("Satellite", MeasureColumnHeaderWidth(listView, "Satellite"));
      listView.Columns.Add("Transmitter", 130);
      listView.Columns.Add("Next Pass", MeasureNextPassColumnWidth(listView));
      listView.Columns[NextColumnIndex].TextAlign = HorizontalAlignment.Right;
      listView.Columns.Add("Max", 50);
      listView.Columns[MaxColumnIndex].TextAlign = HorizontalAlignment.Right;
      listView.Columns.Add("Audio", MeasureColumnHeaderWidth(listView, "Audio"));
      listView.Columns.Add("I/Q", 45);
      listView.Columns.Add("Rotator", MeasureColumnHeaderWidth(listView, "Rotator"));
      listView.DoubleClick += (s, e) => SelectInApp();
      listView.MouseMove += ListView_MouseMove;
      listView.MouseLeave += ListView_MouseLeave;
      listView.MouseDown += ListView_MouseDown;
      listView.ItemDrag += ListView_ItemDrag;
      listView.DragEnter += ListView_DragEnter;
      listView.DragOver += ListView_DragOver;
      listView.DragDrop += ListView_DragDrop;
      listView.DragLeave += (s, e) => DragSourceIndex = null;

      Controls.Add(listView);
      Controls.Add(top);
      Controls.Add(presetBar);

      RefreshPresetList();
      ctx.Settings.Ui.RestoreColumnWidths("MonitoredSatellitesPanel", listView);
    }

    private void RefreshPresetList()
    {
      refreshingPresets = true;
      try
      {
        string? selectedId = ctx.MonitoredSatellites.CurrentList?.Id;
        presetComboBox.Items.Clear();
        foreach (var list in ctx.MonitoredSatellites.Lists)
          presetComboBox.Items.Add(list);

        int idx = ctx.MonitoredSatellites.Lists.FindIndex(l => l.Id == selectedId);
        presetComboBox.SelectedIndex = idx >= 0 ? idx : 0;
      }
      finally
      {
        refreshingPresets = false;
      }
    }

    private void PresetComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
      if (refreshingPresets) return;
      if (presetComboBox.SelectedItem is not MonitoredSatelliteList list) return;
      if (list.Id == ctx.MonitoredSatellites.SelectedListId) return;

      ctx.MonitoredSatellites.SelectList(list.Id);
      ctx.MonitoredPasses?.FullRebuild();
      RefreshList();
      ctx.MonitoredSatellites.ApplyMonitoredTransmitterForSelectedSat(ctx);
    }

    private void NewListBtn_Click(object? sender, EventArgs e)
    {
      string? name = TextInputForm.PromptForText(this, "New Monitored List", "List name:", "New List");
      if (string.IsNullOrWhiteSpace(name)) return;

      ctx.MonitoredSatellites.CreateList(name);
      ctx.MonitoredPasses?.FullRebuild();
      RefreshPresetList();
      RefreshList();
    }

    private void CopyListBtn_Click(object? sender, EventArgs e)
    {
      var source = ctx.MonitoredSatellites.CurrentList;
      if (source == null) return;

      string? name = TextInputForm.PromptForText(this, "Copy Monitored List", "New list name:", $"{source.Name} copy");
      if (string.IsNullOrWhiteSpace(name)) return;

      ctx.MonitoredSatellites.CloneList(source, name);
      ctx.MonitoredPasses?.FullRebuild();
      RefreshPresetList();
      RefreshList();
    }

    private void RenameListBtn_Click(object? sender, EventArgs e)
    {
      var list = ctx.MonitoredSatellites.CurrentList;
      if (list == null) return;

      string? name = TextInputForm.PromptForText(this, "Rename Monitored List", "List name:", list.Name);
      if (string.IsNullOrWhiteSpace(name)) return;
      if (name.Trim() == list.Name) return;

      ctx.MonitoredSatellites.RenameList(list, name);
      RefreshPresetList();
    }

    public void RefreshList()
    {
      if (IsDisposed) return;

      HideSkyMapPopup();
      HideTransmitterPopup();
      nextPassTimer.Stop();
      refreshingList = true;
      listView.BeginUpdate();
      try
      {
        listView.Items.Clear();

        if (ctx.SatnogsDb == null)
          return;

        var entries = ctx.MonitoredSatellites.CurrentEntries;

        for (int i = 0; i < entries.Count; i++)
        {
          var entry = entries[i];
          var sat = ctx.SatnogsDb?.GetSatellite(entry.SatelliteId);
          if (sat == null) continue;

          string txName = "";
          if (!string.IsNullOrEmpty(entry.TransmitterId))
          {
            var tx = sat.Transmitters.FirstOrDefault(t => t.uuid == entry.TransmitterId);
            txName = tx?.description ?? "";
          }
          if (string.IsNullOrEmpty(txName) && sat.Transmitters.Count > 0)
            txName = sat.Transmitters[0].description;

          string audio = entry.AutoRecordMode == AutoRecordMode.Audio ? "☑" : "☐";
          string iq = entry.AutoRecordMode == AutoRecordMode.Iq ? "☑" : "☐";
          string rotator = entry.AutoRotator ? "☑" : "☐";

          var item = new ListViewItem([(i + 1).ToString(), sat.name, txName, "", "", audio, iq, rotator]);
          item.Tag = entry;
          listView.Items.Add(item);
        }
      }
      finally
      {
        listView.EndUpdate();
        refreshingList = false;
        if (!IsDisposed) nextPassTimer.Start();
      }

      ResizeSatelliteColumnIfNeeded();
      UpdateNextPassCountdown();
    }

    public void ShowSelectedSatellite()
    {
      UpdateRowHighlights();
    }

    private bool HasSavedColumnWidths =>
      ctx.Settings.Ui.ListViewColumnWidths.ContainsKey("MonitoredSatellitesPanel");

    private void ResizeSatelliteColumnIfNeeded()
    {
      if (HasSavedColumnWidths) return;
      ResizeSatelliteColumn();
    }

    private void ResizeSatelliteColumn()
    {
      if (listView.Columns.Count <= SatelliteColumnIndex) return;
      listView.Columns[SatelliteColumnIndex].Width = MeasureSatelliteColumnWidth(listView);
    }

    private void UpdateNextPassCountdown()
    {
      if (IsDisposed || refreshingList) return;
      if (ctx.MonitoredPasses == null) return;

      var now = DateTime.UtcNow;
      var passes = ctx.MonitoredPasses.GetPassesSnapshot();
      string? nextAutoId = GetNextAutoMonitoredSatelliteId(passes, now);
      var selected = ctx.SatelliteSelector.SelectedSatellite;

      foreach (ListViewItem item in listView.Items)
      {
        if (item.Tag is not MonitoredSatelliteEntry entry) continue;
        var sat = ctx.SatnogsDb?.GetSatellite(entry.SatelliteId);
        if (sat == null || item.SubItems.Count <= RotatorColumnIndex) continue;

        var active = passes.FirstOrDefault(p => p.Satellite == sat && p.StartTime <= now && p.EndTime > now);
        if (active != null)
        {
          SetSubItemText(item, NextColumnIndex, $"Now {Utils.TimespanToString(active.EndTime - now)}");
          SetSubItemText(item, MaxColumnIndex, $"{Math.Round(active.MaxElevation):F0}°");
        }
        else
        {
          var next = passes
            .Where(p => p.Satellite == sat && p.StartTime > now)
            .OrderBy(p => p.StartTime)
            .FirstOrDefault();

          SetSubItemText(item, NextColumnIndex, next == null ? "" : Utils.TimespanToString(next.StartTime - now));
          SetSubItemText(item, MaxColumnIndex, next == null ? "" : $"{Math.Round(next.MaxElevation):F0}°");
        }

        ApplyItemHighlight(item, sat == selected && active != null,
          nextAutoId != null && entry.SatelliteId == nextAutoId);
      }
    }

    private void UpdateRowHighlights()
    {
      if (IsDisposed || refreshingList) return;
      if (ctx.MonitoredPasses == null) return;

      var now = DateTime.UtcNow;
      var passes = ctx.MonitoredPasses.GetPassesSnapshot();
      string? nextAutoId = GetNextAutoMonitoredSatelliteId(passes, now);
      var selected = ctx.SatelliteSelector.SelectedSatellite;

      foreach (ListViewItem item in listView.Items)
      {
        if (item.Tag is not MonitoredSatelliteEntry entry) continue;
        var sat = ctx.SatnogsDb?.GetSatellite(entry.SatelliteId);
        if (sat == null) continue;
        bool active = passes.Any(p => p.Satellite == sat && p.StartTime <= now && p.EndTime > now);
        ApplyItemHighlight(item, sat == selected && active, nextAutoId != null && entry.SatelliteId == nextAutoId);
      }
    }

    private string? GetNextAutoMonitoredSatelliteId(IReadOnlyList<SatellitePass> passes, DateTime now)
    {
      if (!ctx.Settings.Satellites.AutoMonitorEnabled) return null;

      int minEl = Math.Max(0, Math.Min(90, ctx.Settings.Satellites.AutoMonitorMinElevationDeg));
      string? current = MonitoredSatellitesStore.GetAutoMonitoredSatelliteId(
        ctx.MonitoredSatellites.CurrentEntries, passes, now, minEl);
      string? next = ctx.MonitoredSatellites.PredictNextAutoMonitoredSatelliteId(
        passes, now, minEl, autoMonitorEnabled: true);

      if (next != null && next == current) return null;
      return next;
    }

    private static void SetSubItemText(ListViewItem item, int index, string text)
    {
      if (item.SubItems[index].Text != text)
        item.SubItems[index].Text = text;
    }

    private static void ApplyItemHighlight(ListViewItem item, bool selected, bool nextAutoMonitor)
    {
      Color color = selected ? SelectedSatBackColor
        : nextAutoMonitor ? NextMonitoredBackColor
        : SystemColors.Window;
      // fixed: LightGreen / LightGoldenrodYellow stay light in both themes, so black text
      // is the only color that stays readable when WindowText flips in dark mode
      Color text = selected || nextAutoMonitor ? Color.Black : SystemColors.WindowText;

      if (!item.UseItemStyleForSubItems) item.UseItemStyleForSubItems = true;
      if (item.BackColor != color) item.BackColor = color;
      if (item.ForeColor != text) item.ForeColor = text;
    }

    private static void EnableDoubleBuffering(ListView listView)
    {
      typeof(ListView)
        .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?.SetValue(listView, true);
    }

    private void ListView_MouseMove(object? sender, MouseEventArgs e)
    {
      if (TryHoverSkyMap(e.Location))
      {
        HideTransmitterPopup();
        ClearListColumnToolTip();
        return;
      }

      if (TryHoverTransmitter(e.Location))
      {
        HideSkyMapPopup();
        ClearListColumnToolTip();
        return;
      }

      HideSkyMapPopup();
      HideTransmitterPopup();
      string tip = GetColumnToolTip(e.Location);
      if (listColumnToolTip.GetToolTip(listView) != tip)
        listColumnToolTip.SetToolTip(listView, tip);
    }

    private void ListView_MouseLeave(object? sender, EventArgs e)
    {
      HideSkyMapPopup();
      HideTransmitterPopup();
      listColumnToolTip.SetToolTip(listView, "");
    }

    private void ClearListColumnToolTip()
    {
      if (listColumnToolTip.GetToolTip(listView) != "")
        listColumnToolTip.SetToolTip(listView, "");
    }

    // true when the pointer is on a Max cell that has a current or next pass to plot
    private bool TryHoverSkyMap(Point location)
    {
      if (!TryHitDataCell(location, MaxColumnIndex, out var item, out var subItem)) return false;
      if (item.Tag is not MonitoredSatelliteEntry entry) return false;

      var pass = PassForEntry(entry);
      if (pass == null) return false;

      if (skyHoverItem == item && ReferenceEquals(skyHoverPass, pass))
      {
        if (skyMapPopup.Visible) return true;
        if (!skyMapTimer.Enabled) skyMapTimer.Start();
        return true;
      }

      skyHoverItem = item;
      skyHoverPass = pass;
      if (skyMapPopup.Visible)
        ShowSkyMapPopup(pass, subItem.Bounds);
      else
      {
        skyMapTimer.Stop();
        skyMapTimer.Start();
      }
      return true;
    }

    // true when the pointer is on a Transmitter cell that has a SatNOGS transmitter to describe
    private bool TryHoverTransmitter(Point location)
    {
      if (!TryHitDataCell(location, TransmitterColumnIndex, out var item, out var subItem)) return false;
      if (item.Tag is not MonitoredSatelliteEntry entry) return false;

      var tx = TransmitterForEntry(entry);
      if (tx == null) return false;

      if (transmitterHoverItem == item && ReferenceEquals(transmitterHoverTx, tx))
      {
        if (transmitterPopup.Visible) return true;
        if (!transmitterTimer.Enabled) transmitterTimer.Start();
        return true;
      }

      transmitterHoverItem = item;
      transmitterHoverTx = tx;
      if (transmitterPopup.Visible)
        ShowTransmitterPopup(tx, subItem.Bounds);
      else
      {
        transmitterTimer.Stop();
        transmitterTimer.Start();
      }
      return true;
    }

    private bool TryHitDataCell(Point location, int column, out ListViewItem item, out ListViewItem.ListViewSubItem subItem)
    {
      var hit = listView.HitTest(location);
      item = hit.Item!;
      subItem = hit.SubItem!;
      if (IsOverColumnHeader(location)) return false;
      if (hit.Item == null || hit.SubItem == null) return false;
      int col = hit.Item.SubItems.IndexOf(hit.SubItem);
      if (col != column) return false;
      return hit.SubItem.Bounds.Contains(location);
    }

    private void SkyMapTimer_Tick(object? sender, EventArgs e)
    {
      skyMapTimer.Stop();
      if (skyHoverItem == null || skyHoverPass == null) return;
      if (skyHoverItem.ListView != listView) return;
      ShowSkyMapPopup(skyHoverPass, skyHoverItem.SubItems[MaxColumnIndex].Bounds);
    }

    private void ShowSkyMapPopup(SatellitePass pass, Rectangle cell)
    {
      var size = skyMapPopup.CanvasSize;
      var screen = listView.PointToScreen(new Point(cell.Right + 4, cell.Top));
      var area = Screen.FromControl(listView).WorkingArea;
      if (screen.X + size.Width > area.Right)
        screen.X = listView.PointToScreen(cell.Location).X - size.Width - 4;
      if (screen.Y + size.Height > area.Bottom)
        screen.Y = area.Bottom - size.Height;
      if (screen.Y < area.Top) screen.Y = area.Top;
      skyMapPopup.ShowPass(this, pass, screen);
    }

    private void HideSkyMapPopup()
    {
      skyMapTimer.Stop();
      skyHoverItem = null;
      skyHoverPass = null;
      if (skyMapPopup.Visible) skyMapPopup.Hide();
    }

    private void TransmitterTimer_Tick(object? sender, EventArgs e)
    {
      transmitterTimer.Stop();
      if (transmitterHoverItem == null || transmitterHoverTx == null) return;
      if (transmitterHoverItem.ListView != listView) return;
      ShowTransmitterPopup(transmitterHoverTx, transmitterHoverItem.SubItems[TransmitterColumnIndex].Bounds);
    }

    private void ShowTransmitterPopup(SatnogsDbTransmitter tx, Rectangle cell)
    {
      string details = tx.GetTooltipText();
      if (string.IsNullOrWhiteSpace(details)) return;

      var size = transmitterPopup.Measure(details);
      var screen = listView.PointToScreen(new Point(cell.Right + 4, cell.Top));
      var area = Screen.FromControl(listView).WorkingArea;
      if (size.Width > area.Width) size.Width = area.Width;
      if (size.Height > area.Height) size.Height = area.Height;
      transmitterPopup.Size = size;
      if (screen.X + size.Width > area.Right)
        screen.X = listView.PointToScreen(cell.Location).X - size.Width - 4;
      if (screen.X < area.Left) screen.X = area.Left;
      if (screen.Y + size.Height > area.Bottom)
        screen.Y = area.Bottom - size.Height;
      if (screen.Y < area.Top) screen.Y = area.Top;
      transmitterPopup.ShowText(this, screen);
    }

    private void HideTransmitterPopup()
    {
      transmitterTimer.Stop();
      transmitterHoverItem = null;
      transmitterHoverTx = null;
      if (transmitterPopup.Visible) transmitterPopup.Hide();
    }

    private SatnogsDbTransmitter? TransmitterForEntry(MonitoredSatelliteEntry entry)
    {
      var sat = ctx.SatnogsDb?.GetSatellite(entry.SatelliteId);
      if (sat == null) return null;
      if (!string.IsNullOrEmpty(entry.TransmitterId))
      {
        var tx = sat.Transmitters.FirstOrDefault(t => t.uuid == entry.TransmitterId);
        if (tx != null) return tx;
      }
      return sat.Transmitters.FirstOrDefault();
    }

    private SatellitePass? PassForEntry(MonitoredSatelliteEntry entry)
    {
      if (ctx.MonitoredPasses == null) return null;
      var sat = ctx.SatnogsDb?.GetSatellite(entry.SatelliteId);
      if (sat == null) return null;

      var now = DateTime.UtcNow;
      var passes = ctx.MonitoredPasses.GetPassesSnapshot();
      var active = passes.FirstOrDefault(p => p.Satellite == sat && p.StartTime <= now && p.EndTime > now);
      if (active != null) return active;
      return passes
        .Where(p => p.Satellite == sat && p.StartTime > now)
        .OrderBy(p => p.StartTime)
        .FirstOrDefault();
    }

    private string GetColumnToolTip(Point location)
    {
      var hit = listView.HitTest(location);
      int col = -1;
      if (hit.Item != null && hit.SubItem != null)
        col = hit.Item.SubItems.IndexOf(hit.SubItem);
      else if (IsOverColumnHeader(location))
        col = GetColumnIndexAt(location.X);

      return col switch
      {
        MaxColumnIndex => MaxPassToolTip,
        NextColumnIndex => NextPassToolTip,
        AudioColumnIndex => AudioRecordToolTip,
        IqColumnIndex => IqRecordToolTip,
        RotatorColumnIndex => RotatorToolTip,
        _ => "",
      };
    }

    private int GetColumnIndexAt(int x)
    {
      int left = 0;
      for (int i = 0; i < listView.Columns.Count; i++)
      {
        int right = left + listView.Columns[i].Width;
        if (x >= left && x < right) return i;
        left = right;
      }
      return -1;
    }

    private void ListView_MouseDown(object sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;

      var hit = listView.HitTest(e.Location);
      if (hit.Item == null) return;
      if (hit.Item.Tag is not MonitoredSatelliteEntry entry) return;

      int col = hit.Item.SubItems.IndexOf(hit.SubItem);
      if (col == AudioColumnIndex)
        entry.AutoRecordMode = entry.AutoRecordMode == AutoRecordMode.Audio ? AutoRecordMode.Off : AutoRecordMode.Audio;
      else if (col == IqColumnIndex)
        entry.AutoRecordMode = entry.AutoRecordMode == AutoRecordMode.Iq ? AutoRecordMode.Off : AutoRecordMode.Iq;
      else if (col == RotatorColumnIndex)
        entry.AutoRotator = !entry.AutoRotator;
      else
        return;

      ctx.MonitoredSatellites.SaveToFile();
      RefreshList();
    }

    private void SelectInApp()
    {
      if (listView.SelectedItems.Count == 0) return;
      if (listView.SelectedItems[0].Tag is not MonitoredSatelliteEntry entry) return;
      ctx.MonitoredSatellites.ApplyEntryToSelector(ctx, entry);
    }

    private void MoveSelected(int delta)
    {
      if (listView.SelectedItems.Count == 0) return;
      if (listView.SelectedItems[0].Tag is not MonitoredSatelliteEntry entry) return;

      var entries = ctx.MonitoredSatellites.CurrentEntries;
      int idx = entries.IndexOf(entry);
      if (idx < 0) return;
      int newIdx = idx + delta;
      if (newIdx < 0 || newIdx >= entries.Count) return;

      ctx.MonitoredSatellites.MoveEntry(idx, newIdx);

      RefreshList();
      if (listView.Items.Count == 0) return;
      listView.Items[Math.Max(0, Math.Min(newIdx, listView.Items.Count - 1))].Selected = true;
    }

    private void RemoveSelected()
    {
      if (listView.SelectedItems.Count == 0) return;
      if (listView.SelectedItems[0].Tag is not MonitoredSatelliteEntry entry) return;

      ctx.MonitoredSatellites.RemoveEntry(entry.SatelliteId);
      ctx.MonitoredPasses?.FullRebuild();

      RefreshList();
    }

    private void ListView_ItemDrag(object sender, ItemDragEventArgs e)
    {
      if (listView.SelectedItems.Count == 0) return;
      DragSourceIndex = listView.SelectedItems[0].Index;
      DoDragDrop("MonitoredSatellitesPanelReorder", DragDropEffects.Move);
    }

    private void ListView_DragEnter(object sender, DragEventArgs e)
    {
      if (DragSourceIndex == null)
      {
        e.Effect = DragDropEffects.None;
        return;
      }

      e.Effect = DragDropEffects.Move;
    }

    private void ListView_DragOver(object sender, DragEventArgs e)
    {
      if (DragSourceIndex == null)
      {
        e.Effect = DragDropEffects.None;
        return;
      }

      e.Effect = DragDropEffects.Move;
      var pt = listView.PointToClient(new Point(e.X, e.Y));
      var hoverItem = listView.GetItemAt(pt.X, pt.Y);
      if (hoverItem != null)
        listView.InsertionMark.Index = hoverItem.Index;
      else
        listView.InsertionMark.Index = listView.Items.Count;
    }

    private void ListView_DragDrop(object sender, DragEventArgs e)
    {
      if (DragSourceIndex == null) return;

      int src = DragSourceIndex.Value;
      DragSourceIndex = null;
      listView.InsertionMark.Index = -1;

      if (src < 0 || src >= listView.Items.Count) return;

      var pt = listView.PointToClient(new Point(e.X, e.Y));
      var dstItem = listView.GetItemAt(pt.X, pt.Y);
      int dst = dstItem?.Index ?? listView.Items.Count - 1;
      dst = Math.Max(0, Math.Min(dst, listView.Items.Count - 1));
      if (dst == src) return;

      var srcEntry = listView.Items[src].Tag as MonitoredSatelliteEntry;
      if (srcEntry == null) return;

      var entries = ctx.MonitoredSatellites.CurrentEntries;
      int srcIdIndex = entries.IndexOf(srcEntry);
      if (srcIdIndex < 0) return;

      string? dstSatId = (listView.Items[dst].Tag as MonitoredSatelliteEntry)?.SatelliteId;
      if (dstSatId == null) return;
      int dstIdIndex = entries.FindIndex(e => e.SatelliteId == dstSatId);
      if (dstIdIndex < 0) return;

      ctx.MonitoredSatellites.MoveEntry(srcIdIndex, dstIdIndex);
      RefreshList();

      if (listView.Items.Count == 0) return;
      int selectIdx = Math.Max(0, Math.Min(dst, listView.Items.Count - 1));
      listView.Items[selectIdx].Selected = true;
    }

    private static void ConfigureIconButton(Button btn, Image image, string toolTip)
    {
      btn.Text = "";
      btn.Image = image;
      btn.ImageAlign = ContentAlignment.MiddleCenter;
      btn.Size = new Size(28, 28);
      btn.Margin = new Padding(0, 0, 4, 0);
      btn.UseVisualStyleBackColor = true;
      btn.AccessibleName = toolTip;
    }

    private static void ConfigureGlyphButton(Button btn, string mdl2Glyph, string toolTipText, ToolTip? toolTip = null)
    {
      btn.Text = mdl2Glyph;
      btn.Font = new Font("Segoe MDL2 Assets", 10f);
      btn.TextAlign = ContentAlignment.MiddleCenter;
      btn.Size = new Size(28, 28);
      btn.Margin = new Padding(0, 0, 8, 0);
      btn.UseVisualStyleBackColor = true;
      btn.AccessibleName = toolTipText;
      toolTip?.SetToolTip(btn, toolTipText);
    }

    private static Image RotateBitmap(Image source, RotateFlipType rotate)
    {
      var bmp = new Bitmap(source);
      bmp.RotateFlip(rotate);
      return bmp;
    }

    private static int MeasureColumnHeaderWidth(Control control, string headerText)
    {
      int textWidth = TextRenderer.MeasureText(headerText, control.Font).Width;
      return textWidth + 16;
    }

    private static int MeasureNextPassColumnWidth(Control control)
    {
      int headerWidth = MeasureColumnHeaderWidth(control, "Next Pass");
      int sampleWidth = TextRenderer.MeasureText("Now 23h 59m 59s", control.Font).Width + 16;
      return Math.Max(headerWidth, sampleWidth);
    }

    private static int MeasureSatelliteColumnWidth(ListView listView)
    {
      int width = MeasureColumnHeaderWidth(listView, "Satellite");
      foreach (ListViewItem item in listView.Items)
      {
        if (item.SubItems.Count <= SatelliteColumnIndex) continue;
        int nameWidth = TextRenderer.MeasureText(item.SubItems[SatelliteColumnIndex].Text, listView.Font).Width + 16;
        width = Math.Max(width, nameWidth);
      }
      return Math.Min(SatelliteColumnMaxWidth, width);
    }

    private bool IsOverColumnHeader(Point location)
    {
      if (listView.Items.Count > 0)
        return location.Y < listView.GetItemRect(0).Top;
      return location.Y < TextRenderer.MeasureText("Ag", listView.Font).Height + 8;
    }

    // tooltip-style popup that does not capture the mouse, so leaving the Max cell still
    // reaches the list and the plot can close or switch to another row
    private sealed class MiniSkyPopup : Form
    {
      private const int CanvasPixels = 112;
      private const int WsExNoActivate = 0x08000000;
      private const int WsExToolWindow = 0x00000080;
      private const int WsExTransparent = 0x00000020;
      private SatellitePass? pass;

      public MiniSkyPopup()
      {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(CanvasPixels, CanvasPixels);
        BackColor = Theme.TipBack;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
      }

      public Size CanvasSize => Size;

      protected override bool ShowWithoutActivation => true;

      protected override CreateParams CreateParams
      {
        get
        {
          var cp = base.CreateParams;
          cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTransparent;
          return cp;
        }
      }

      public void ShowPass(IWin32Window owner, SatellitePass nextPass, Point screenLocation)
      {
        pass = nextPass;
        Location = screenLocation;
        Invalidate();
        if (!Visible) Show(owner);
      }

      protected override void OnPaint(PaintEventArgs e)
      {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.WindowFrame, ButtonBorderStyle.Solid);
        pass?.DrawMiniSkyView(e.Graphics, new RectangleF(4, 4, Width - 8, Height - 8));
      }
    }

    // same mouse-pass-through host as MiniSkyPopup, showing the SatNOGS transmitter tooltip text
    private sealed class TransmitterTipPopup : Form
    {
      private const int Pad = 8;
      private const int MaxWidth = 480;
      private const int WsExNoActivate = 0x08000000;
      private const int WsExToolWindow = 0x00000080;
      private const int WsExTransparent = 0x00000020;
      private static readonly TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
        TextFormatFlags.WordBreak;
      private string text = "";

      public TransmitterTipPopup()
      {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.TipBack;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
      }

      protected override bool ShowWithoutActivation => true;

      protected override CreateParams CreateParams
      {
        get
        {
          var cp = base.CreateParams;
          cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTransparent;
          return cp;
        }
      }

      public Size Measure(string details)
      {
        text = details;
        var measured = TextRenderer.MeasureText(text, SystemFonts.StatusFont, new Size(MaxWidth, int.MaxValue), TextFlags);
        Size = new Size(measured.Width + Pad * 2, measured.Height + Pad * 2);
        return Size;
      }

      public void ShowText(IWin32Window owner, Point screenLocation)
      {
        Location = screenLocation;
        Invalidate();
        if (!Visible) Show(owner);
      }

      protected override void OnPaint(PaintEventArgs e)
      {
        e.Graphics.Clear(Theme.TipBack);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.WindowFrame, ButtonBorderStyle.Solid);
        TextRenderer.DrawText(e.Graphics, text, SystemFonts.StatusFont,
          new Rectangle(Pad, Pad, Width - Pad * 2, Height - Pad * 2),
          Theme.TipText, TextFlags);
      }
    }
  }
}

