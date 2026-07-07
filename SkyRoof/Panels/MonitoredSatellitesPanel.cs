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
    private readonly System.Windows.Forms.Timer nextPassTimer = new();
    private readonly System.Windows.Forms.Timer minElSaveTimer = new() { Interval = 400 };
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
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      Log.Information("Closing MonitoredSatellitesPanel");
      nextPassTimer.Stop();
      nextPassTimer.Dispose();
      minElSaveTimer.Stop();
      minElSaveTimer.Dispose();
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
      listView.MouseLeave += (s, e) => listColumnToolTip.SetToolTip(listView, "");
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

      if (!item.UseItemStyleForSubItems) item.UseItemStyleForSubItems = true;
      if (item.BackColor != color) item.BackColor = color;
      if (item.ForeColor != SystemColors.WindowText) item.ForeColor = SystemColors.WindowText;
    }

    private static void EnableDoubleBuffering(ListView listView)
    {
      typeof(ListView)
        .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?.SetValue(listView, true);
    }

    private void ListView_MouseMove(object? sender, MouseEventArgs e)
    {
      string tip = GetColumnToolTip(e.Location);
      if (listColumnToolTip.GetToolTip(listView) != tip)
        listColumnToolTip.SetToolTip(listView, tip);
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
  }
}

