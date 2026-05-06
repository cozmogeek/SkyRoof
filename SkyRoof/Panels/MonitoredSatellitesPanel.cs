using Serilog;
using WeifenLuo.WinFormsUI.Docking;
using VE3NEA;

namespace SkyRoof
{
  public class MonitoredSatellitesPanel : DockContent
  {
    private readonly Context ctx;
    private readonly ListView listView = new();
    private readonly Button upBtn = new();
    private readonly Button downBtn = new();
    private readonly Button removeBtn = new();
    private readonly CheckBox autoCheckbox = new();
    private readonly Label minElLabel = new();
    private readonly TrackBar minElTrackBar = new();
    private readonly Label minElValueLabel = new();
    private readonly System.Windows.Forms.Timer nextPassTimer = new();
    private int? DragSourceIndex;
    private bool syncingAutoControls;

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
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      Log.Information("Closing MonitoredSatellitesPanel");
      nextPassTimer.Stop();
      nextPassTimer.Dispose();
      ctx.MonitoredSatellitesPanel = null;
      ctx.MainForm.MonitoredSatellitesMNU.Checked = false;
      base.OnFormClosing(e);
    }

    private void BuildUi()
    {
      var top = new FlowLayoutPanel
      {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8, 6, 8, 6),
      };

      upBtn.Text = "Up";
      upBtn.AutoSize = true;
      upBtn.Margin = new Padding(0, 0, 8, 0);
      upBtn.Click += (s, e) => MoveSelected(-1);

      downBtn.Text = "Down";
      downBtn.AutoSize = true;
      downBtn.Margin = new Padding(0, 0, 8, 0);
      downBtn.Click += (s, e) => MoveSelected(+1);

      removeBtn.Text = "Remove";
      removeBtn.AutoSize = true;
      removeBtn.Margin = new Padding(0);
      removeBtn.Click += (s, e) => RemoveSelected();

      top.Controls.Add(upBtn);
      top.Controls.Add(downBtn);
      top.Controls.Add(removeBtn);

      autoCheckbox.Text = "Auto";
      autoCheckbox.AutoSize = true;
      autoCheckbox.Margin = new Padding(12, 3, 0, 0);
      autoCheckbox.Checked = ctx.Settings.Satellites.AutoMonitorEnabled;
      autoCheckbox.CheckedChanged += (s, e) =>
      {
        if (syncingAutoControls) return;
        ctx.Settings.Satellites.AutoMonitorEnabled = autoCheckbox.Checked;
        ctx.Settings.SaveToFile();
        if (!autoCheckbox.Checked) ctx.AutoRecorder?.Stop();
        ctx.MainForm?.UpdateAutoMonitorBannerVisibility();
      };
      top.Controls.Add(autoCheckbox);

      minElLabel.Text = "Min El:";
      minElLabel.AutoSize = true;
      minElLabel.Margin = new Padding(12, 6, 4, 0);
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
        ctx.Settings.SaveToFile();
      };
      top.Controls.Add(minElTrackBar);

      minElValueLabel.AutoSize = true;
      minElValueLabel.Margin = new Padding(0, 6, 0, 0);
      top.Controls.Add(minElValueLabel);

      listView.Dock = DockStyle.Fill;
      listView.View = View.Details;
      listView.FullRowSelect = true;
      listView.MultiSelect = false;
      listView.AllowDrop = true;
      listView.Columns.Add("#", 40);
      listView.Columns.Add("Satellite", 200);
      listView.Columns.Add("Transmitter", 220);
      listView.Columns.Add("Max", 50);
      listView.Columns.Add("Next", 110);
      listView.Columns.Add("Audio", 55);
      listView.Columns.Add("I/Q", 45);
      listView.DoubleClick += (s, e) => SelectInApp();
      listView.MouseDown += ListView_MouseDown;
      listView.ItemDrag += ListView_ItemDrag;
      listView.DragEnter += ListView_DragEnter;
      listView.DragOver += ListView_DragOver;
      listView.DragDrop += ListView_DragDrop;
      listView.DragLeave += (s, e) => DragSourceIndex = null;

      Controls.Add(listView);
      Controls.Add(top);
    }

    public void SyncAutoMonitorControlsFromSettings()
    {
      syncingAutoControls = true;
      try
      {
        autoCheckbox.Checked = ctx.Settings.Satellites.AutoMonitorEnabled;
      }
      finally
      {
        syncingAutoControls = false;
      }
    }

    public void RefreshList()
    {
      listView.BeginUpdate();
      try
      {
        listView.Items.Clear();

        var ids = ctx.Settings.Satellites.MonitoredSatelliteIds;

        for (int i = 0; i < ids.Count; i++)
        {
          var sat = ctx.SatnogsDb?.GetSatellite(ids[i]);
          if (sat == null) continue;

          var cust = ctx.Settings.Satellites.SatelliteCustomizations.GetOrCreate(sat.sat_id);
          string txName = "";
          if (!string.IsNullOrEmpty(cust.SelectedTransmitterId))
          {
            var tx = sat.Transmitters.FirstOrDefault(t => t.uuid == cust.SelectedTransmitterId);
            txName = tx?.description ?? "";
          }
          if (string.IsNullOrEmpty(txName) && sat.Transmitters.Count > 0)
            txName = sat.Transmitters[0].description;

          string audio = cust.AutoRecordMode == AutoRecordMode.Audio ? "☑" : "☐";
          string iq = cust.AutoRecordMode == AutoRecordMode.Iq ? "☑" : "☐";

          var item = new ListViewItem([(i + 1).ToString(), sat.name, txName, "", "", audio, iq]);
          item.Tag = sat;
          listView.Items.Add(item);
        }
      }
      finally
      {
        listView.EndUpdate();
      }

      UpdateNextPassCountdown();
    }

    private void UpdateNextPassCountdown()
    {
      if (IsDisposed) return;
      if (ctx.MonitoredPasses == null) return;

      var now = DateTime.UtcNow;
      var passes = ctx.MonitoredPasses.Passes;

      foreach (ListViewItem item in listView.Items)
      {
        if (item.Tag is not SatnogsDbSatellite sat) continue;

        var active = passes.FirstOrDefault(p => p.Satellite == sat && p.StartTime <= now && p.EndTime > now);
        if (active != null)
        {
          item.SubItems[3].Text = $"{Math.Round(active.MaxElevation):F0}°";
          item.SubItems[4].Text = "Now";
          continue;
        }

        var next = passes
          .Where(p => p.Satellite == sat && p.StartTime > now)
          .OrderBy(p => p.StartTime)
          .FirstOrDefault();

        item.SubItems[3].Text = next == null ? "" : $"{Math.Round(next.MaxElevation):F0}°";
        item.SubItems[4].Text = next == null ? "" : Utils.TimespanToString(next.StartTime - now);
      }
    }

    private void ListView_MouseDown(object sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;

      var hit = listView.HitTest(e.Location);
      if (hit.Item == null) return;
      if (hit.Item.Tag is not SatnogsDbSatellite sat) return;

      // columns: 0=#, 1=sat, 2=tx, 3=max, 4=next, 5=audio, 6=iq
      int col = hit.Item.SubItems.IndexOf(hit.SubItem);
      if (col != 5 && col != 6) return;

      var cust = ctx.Settings.Satellites.SatelliteCustomizations.GetOrCreate(sat.sat_id);
      if (col == 5)
        cust.AutoRecordMode = cust.AutoRecordMode == AutoRecordMode.Audio ? AutoRecordMode.Off : AutoRecordMode.Audio;
      else
        cust.AutoRecordMode = cust.AutoRecordMode == AutoRecordMode.Iq ? AutoRecordMode.Off : AutoRecordMode.Iq;

      ctx.Settings.SaveToFile();
      RefreshList();
    }

    private void SelectInApp()
    {
      if (listView.SelectedItems.Count == 0) return;
      var sat = listView.SelectedItems[0].Tag as SatnogsDbSatellite;
      if (sat == null) return;
      ctx.SatelliteSelector.SetSelectedSatellite(sat);
    }

    private void MoveSelected(int delta)
    {
      if (listView.SelectedItems.Count == 0) return;
      var sat = listView.SelectedItems[0].Tag as SatnogsDbSatellite;
      if (sat == null) return;

      var ids = ctx.Settings.Satellites.MonitoredSatelliteIds;
      int idx = ids.IndexOf(sat.sat_id);
      if (idx < 0) return;
      int newIdx = idx + delta;
      if (newIdx < 0 || newIdx >= ids.Count) return;

      (ids[idx], ids[newIdx]) = (ids[newIdx], ids[idx]);
      ctx.Settings.SaveToFile();

      RefreshList();
      listView.Items[Math.Max(0, Math.Min(newIdx, listView.Items.Count - 1))].Selected = true;
    }

    private void RemoveSelected()
    {
      if (listView.SelectedItems.Count == 0) return;
      var sat = listView.SelectedItems[0].Tag as SatnogsDbSatellite;
      if (sat == null) return;

      ctx.Settings.Satellites.MonitoredSatelliteIds.RemoveAll(id => id == sat.sat_id);
      ctx.Settings.SaveToFile();
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

      var srcSat = listView.Items[src].Tag as SatnogsDbSatellite;
      if (srcSat == null) return;

      var ids = ctx.Settings.Satellites.MonitoredSatelliteIds;
      int srcIdIndex = ids.IndexOf(srcSat.sat_id);
      if (srcIdIndex < 0) return;

      // Map ListView dst index to sat id
      string? dstSatId = (listView.Items[dst].Tag as SatnogsDbSatellite)?.sat_id;
      if (dstSatId == null) return;
      int dstIdIndex = ids.IndexOf(dstSatId);
      if (dstIdIndex < 0) return;

      // Move the id in the priority list
      var moved = ids[srcIdIndex];
      ids.RemoveAt(srcIdIndex);
      if (dstIdIndex > srcIdIndex) dstIdIndex -= 1;
      ids.Insert(dstIdIndex, moved);

      ctx.Settings.SaveToFile();
      RefreshList();

      int selectIdx = Math.Max(0, Math.Min(dst, listView.Items.Count - 1));
      listView.Items[selectIdx].Selected = true;
    }
  }
}

