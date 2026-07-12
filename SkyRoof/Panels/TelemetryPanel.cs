using MathNet.Numerics;
using Newtonsoft.Json;
using Serilog;
using SkyRoof.Satellites;
using VE3NEA;
using VE3NEA.SkySSTV;
using VE3NEA.SkyTlm.Core;
using VE3NEA.SkyTlm.Deframing;
using VE3NEA.SkyTlm.Telemetry;
using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof
{
  public partial class TelemetryPanel : DockContent
  {
    private static readonly JsonSerializerSettings SerializerParams =
      new() { NullValueHandling = NullValueHandling.Ignore };

    private readonly Context ctx;
    private SatnogsDbSatellite? Satellite;
    private SatnogsDbTransmitter Transmitter;
    private bool Terrestrial;
    private bool SatAboveHorizon = false;
    private SignalParams? SignalParams;
    private TelemetryDecocder? Decoder;
    private SatnogsUploader? SatnogsUploader;
    private TelemetryRegistry? TelemetryRegistry;
    // the most recently added tree node: either the pass node itself (before it has any leaves) or its
    // last-added leaf. the pass node a frame/image belongs to is always this node's parent, or the node
    // itself when it has no leaves yet — so a single field tracks both "current pass" and "last leaf"
    private TreeNode? Current;
    private TreeNode? CurrentPassNode => Current?.Parent ?? Current;
    private ILogger? FrameLogger;
    private DecodeSnapshot? CurrentDecode;

    // Identity of the transmitter a decoder was built for, captured when the pipeline is created and bound to
    // that pipeline's event handlers. Frames surface on the decode worker thread, possibly after the user has
    // switched to a different transmitter; carrying the snapshot with the frame keeps it attributed to the
    // transmitter that actually produced it instead of to whatever is selected when the frame arrives.
    private sealed class DecodeSnapshot
    {
      internal readonly SatnogsDbSatellite? Satellite;
      internal readonly SatnogsDbTransmitter Transmitter;
      internal readonly SignalParams SignalParams;

      internal DecodeSnapshot(SatnogsDbSatellite? satellite, SatnogsDbTransmitter transmitter, SignalParams signalParams)
      {
        Satellite = satellite;
        Transmitter = transmitter;
        SignalParams = signalParams;
      }
    }

    // one progressively-built SSTV image: the tree node's Tag, updated in place as ImageUpdated events
    // re-render lines, finalized (and auto-saved) on ImageCompleted
    private sealed class SstvImageInfo
    {
      internal readonly DecodeSnapshot Snapshot;
      internal readonly DateTime FirstSeen = DateTime.Now;
      internal SstvImageEvent Event;
      internal Bitmap? Bitmap;
      internal string? SavedPath;

      internal SstvImageInfo(DecodeSnapshot snapshot, SstvImageEvent evt)
      {
        Snapshot = snapshot;
        Event = evt;
      }

      internal string Describe()
      {
        return
          $"Sat: {Snapshot.Transmitter?.Satellite?.name ?? "Unknown"}\r\n" +
          $"Tx: {Snapshot.Transmitter?.description}\r\n" +
          $"Mode: {Event.Mode}\r\n" +
          $"VIS: {(Event.FromVis ? "decoded" : "not decoded, mode from sync cadence")}\r\n" +
          $"Rows: {Event.ValidRows} of {Event.Image.Height}\r\n" +
          $"Status: {(Event.Final ? "complete" : "receiving...")}\r\n" +
          (SavedPath != null ? $"Saved: {SavedPath}\r\n" : "");
      }
    }

    internal class TxPassInfo
    {
      internal DateTime StartTime = DateTime.Now;
      internal SatnogsDbTransmitter Transmitter;
      internal int Orbit;
      internal SignalParams? SignalParams;
      internal int BurstCount = 0;
      internal int FrameCount = 0;
      internal int ImageCount = 0;
      internal bool HasValidFrame = false;

      internal TxPassInfo(SatnogsDbTransmitter transmitter, int orbit)
      {
        Transmitter = transmitter;
        Orbit = orbit;
      }

      internal bool IsSame(SatnogsDbTransmitter transmitter, int orbit)
      {
        return Transmitter.uuid == transmitter.uuid && Orbit == orbit;
      }

      internal string Describe()
      {
        string paramsStr = JsonConvert.SerializeObject(SignalParams, Formatting.Indented, SerializerParams);

        return
          $"Start: {StartTime:yyyy-MM-dd HH:mm:ss}\n" +
          $"Sat: {Transmitter?.Satellite?.name ?? "Unknown"}\n" +
          $"Tx: {Transmitter.description}\n" +
          $"Orbit: {Orbit}\n\n" +
          $"Bursts: {BurstCount}\n" +
          $"Frames: {FrameCount}\n" +
          $"Images: {ImageCount}\n\n" +
          $"Params:{paramsStr}";
      }
    }


    //----------------------------------------------------------------------------------------------
    //                                         system
    //----------------------------------------------------------------------------------------------
    // only for designer
    public TelemetryPanel()
    {
      InitializeComponent();
    }

    public TelemetryPanel(Context ctx)
    {
      Log.Information("Creating TelemetryPanel");
      this.ctx = ctx;

      InitializeComponent();

      string path = Path.Combine(Utils.GetUserDataFolder(), "TelemetryRegistry");
      TelemetryRegistry = new TelemetryRegistry(path);

      ctx.TelemetryPanel = this;
      ctx.MainForm.TelemetryMNU.Checked = true;

      SatnogsUploader = new SatnogsUploader(ctx);

      SetTransmitter();
    }

    private void TelemetryPanel_Shown(object? sender, EventArgs e)
    {
      splitContainer1.SplitterDistance = ctx.Settings.Telemetry.SplitterDistance;
      ImageSplitContainer.SplitterDistance = ctx.Settings.Telemetry.ImageSplitterDistance;
    }

    private void TelemetryPanel_FormClosing(object sender, FormClosingEventArgs e)
    {
      Log.Information("Closing TelemetryPanel");
      ctx.TelemetryPanel = null;
      ctx.MainForm.TelemetryMNU.Checked = false;
      ctx.Settings.Telemetry.SplitterDistance = splitContainer1.SplitterDistance;
      ctx.Settings.Telemetry.ImageSplitterDistance = ImageSplitContainer.SplitterDistance;

      // stop and free the decode pipeline (joins its worker thread and releases native FFTW memory)
      Decoder?.Dispose();
      Decoder = null;
      CurrentDecode = null;

      SatnogsUploader?.Dispose();
      SatnogsUploader = null;

      (FrameLogger as IDisposable)?.Dispose();
      FrameLogger = null;
    }




    //----------------------------------------------------------------------------------------------
    //                                     pipeline
    //----------------------------------------------------------------------------------------------
    internal void SetTransmitter()
    {
      Satellite = ctx.SatelliteSelector.SelectedSatellite;
      Transmitter = ctx.SatelliteSelector.SelectedTransmitter;
      Terrestrial = ctx.FrequencyControl.RadioLink.IsTerrestrial;

      if (Terrestrial) SatNameLabel.Text = "Terrestrial";
      else SatNameLabel.Text = $"{Satellite.name}  {Transmitter.description}";

      ResolveSignalParams();
      UpdateTxStatus();
      CreatDestroyPipeline();
    }

    private void ResolveSignalParams()
    {
      if (Terrestrial)
      {
        SignalParams = null;
        return;
      }

      SignalParams = SignalParamsResolver.Resolve(Transmitter);
      UpdateParamsTooltip();
    }

    /// <summary>Refresh the params tooltip on both status labels: the resolved <see cref="SignalParams"/> (which
    /// picks up the actual deviation the pipeline resolves for a blind FSK burst) plus the name of the telemetry
    /// field-decoder definition its frames will be parsed with. Re-called when a frame arrives so the actual
    /// deviation replaces the initial one once the pipeline locks it.</summary>
    private void UpdateParamsTooltip()
    {
      string tooltip;
      if (SignalParams == null)
        tooltip = "Parameters unknown";
      else
      {
        string paramsStr = JsonConvert.SerializeObject(SignalParams, Formatting.Indented, SerializerParams);
        string telemetry = TelemetryRegistry?.ForNorad(Satellite?.norad_cat_id)?.Id ?? "none";
        tooltip = $"{paramsStr}\nTelemetry: {telemetry}";
      }
      toolTip1.SetToolTip(SatNameLabel, tooltip);
      toolTip1.SetToolTip(StatusLabel, tooltip);
    }

    private void CreatDestroyPipeline()
    {
      bool needPipeline = !Terrestrial && IsDecodable() && SatAboveHorizon;

      // keep the existing decoder only if it was built for the currently selected transmitter. a transmitter
      // change must rebuild the pipeline: otherwise it keeps decoding with the previous transmitter's params
      // and its frames get attributed to the newly selected transmitter (wrong sat/norad/telemetry parser).
      bool matches = Decoder != null && CurrentDecode != null && CurrentDecode.Transmitter.uuid == Transmitter.uuid;

      if (needPipeline && matches) return;
      if (!needPipeline && Decoder == null) return;

      // destroy the existing decoder: purge its queued IQ (recorded for the old transmitter / tuning) so the
      // backlog is discarded rather than decoded and mis-attributed, then dispose it
      if (Decoder != null)
      {
        Decoder.Purge();
        var old = Decoder;
        Decoder = null;
        CurrentDecode = null;
        old.Dispose();
      }

      // create the new decoder, binding the current selection to its handlers so every frame it emits is
      // attributed to this transmitter even if the selection changes while the frame is in flight
      if (needPipeline)
      {
        var snapshot = new DecodeSnapshot(Satellite, Transmitter, SignalParams!);
        CurrentDecode = snapshot;
        Decoder = new(SignalParams!, IsTelemetryDecodable(), IsSstvDecodable());
        if (Decoder.Pipeline != null)
        {
          Decoder.Pipeline.FrameDecoded += frame => FrameDecodedHandler(frame, snapshot);
          Decoder.Pipeline.BurstDecoded += report => BurstDecodedHandler(report, snapshot);
        }
        if (Decoder.Sstv != null)
        {
          // the image-id → tree-node map lives in the subscription closure, so images from a disposed
          // decoder's flush can never collide with ids of the next decoder's images
          var imageNodes = new Dictionary<int, TreeNode>();
          Decoder.Sstv.ImageUpdated += evt => SstvImageHandler(evt, snapshot, imageNodes);
          Decoder.Sstv.ImageCompleted += evt => SstvImageHandler(evt, snapshot, imageNodes);
        }
      }
    }

    private readonly static Modulation[] SupportedModulations = {
      Modulation.FSK,
      Modulation.GFSK,
      Modulation.GMSK,
      Modulation.BPSK,
      Modulation.AFSK
    };

    private bool IsDecodable()
    {
      return IsTelemetryDecodable() || IsSstvDecodable();
    }

    private bool IsTelemetryDecodable()
    {
      if (SignalParams == null) return false;
      if (SignalParams.Framing == Framing.Unknown || SignalParams.Modulation == Modulation.Unknown || SignalParams.Baud == 0) return false;
      return SupportedModulations.Contains(SignalParams.Modulation);
    }

    // SSTV needs no framing or baud: the VIS header / sync cadence in the demod domain carries the mode.
    // HasSstv also catches mixed FSK+SSTV transmitters (UmKA-1) that classify as FSK — for
    // those BOTH decoders run concurrently and self-gate on their own signatures.
    private bool IsSstvDecodable()
    {
      if (SignalParams == null) return false;
      return SignalParams.Modulation == Modulation.SSTV || SignalParamsResolver.HasSstv(Transmitter);
    }

    private void BurstDecodedHandler(StreamingBurstReport report, DecodeSnapshot snapshot)
    {
      BeginInvoke(() =>
        {
          StatusLabel.Text = "decoding...";
          // create the pass entry on the first burst (grayed until a valid frame arrives), not on the first frame
          var txPassInfo = EnsureCurrentPassNode(snapshot);
          txPassInfo.BurstCount++;
          // refresh the right panel if this pass entry is the one currently selected
          if (treeView1.SelectedNode == CurrentPassNode) richTextBox1.Text = txPassInfo.Describe();
        }
       );
    }

    private void FrameDecodedHandler(Frame frame, DecodeSnapshot snapshot)
    {
      ctx.KissServer.SendToAll(frame);
      if (snapshot.Satellite?.norad_cat_id is int norad) SatnogsUploader?.Submit(frame, norad);
      BeginInvoke(() => AddFrame(frame, snapshot));
    }




    //----------------------------------------------------------------------------------------------
    //                                       treeview
    //----------------------------------------------------------------------------------------------
    private void AddFrame(Frame frame, DecodeSnapshot snapshot)
    {
      var txPassInfo = EnsureCurrentPassNode(snapshot);

      // un-gray the pass entry once the first valid frame of the pass is decoded
      if (!txPassInfo.HasValidFrame)
      {
        txPassInfo.HasValidFrame = true;
        CurrentPassNode!.ForeColor = Color.Empty;
      }

      string addr = (snapshot.SignalParams.Framing == Framing.AX25G3RUH ? Ax25Address.Describe(frame.Bytes) : "") ?? "";
      string nodeText = $"{DateTime.Now:HH:mm:ss}  {frame.Length} bytes  {addr}";
      var frameNode = new TreeNode(nodeText);
      string frameText = BuildFrameText(frame, snapshot);
      frameNode.Tag = frameText;
      txPassInfo.FrameCount++;

      SaveFrameToFile(frame, addr, frameText, snapshot);

      // the pipeline may have locked a blind FSK burst's actual deviation while decoding this frame — refresh the
      // tooltip so it shows the deviation actually used instead of the initial (unknown) one. only do this for the
      // currently selected transmitter's decoder, so a late frame from a previous transmitter can't overwrite it.
      if (ReferenceEquals(snapshot, CurrentDecode)) UpdateParamsTooltip();

      AddLeaf(CurrentPassNode!, frameNode);
      if (treeView1.SelectedNode == CurrentPassNode) richTextBox1.Text = txPassInfo.Describe();
    }

    /// <summary>Returns the current pass node's info, creating the pass node (grayed until the first valid
    /// frame) when this is the first burst or frame of a new transmitter+orbit pass.</summary>
    private TxPassInfo EnsureCurrentPassNode(DecodeSnapshot snapshot)
    {
      int orbit = ctx.SdrPasses.GetNextPass(snapshot.Satellite)?.OrbitNumber ?? -1;
      var passNode = CurrentPassNode;
      var txPassInfo = (TxPassInfo?)passNode?.Tag;

      if (passNode == null || !(txPassInfo!.IsSame(snapshot.Transmitter, orbit)))
      {
        passNode = new TreeNode($"{DateTime.Now:yyyy-MM-dd HH:mm} {snapshot.Transmitter.Satellite.name}  {snapshot.Transmitter.description}");
        passNode.ForeColor = Color.Gray;
        txPassInfo = new TxPassInfo(snapshot.Transmitter, orbit);
        txPassInfo.SignalParams = snapshot.SignalParams;
        passNode.Tag = txPassInfo;
        treeView1.Nodes.Add(passNode);
        TrackNewNode(passNode);
      }

      return txPassInfo!;
    }

    /// <summary>Selects the newly added node (pass or leaf) if the tree selection was tracking the previously
    /// current node, or nothing was selected at all; otherwise leaves the user's selection alone. WinForms
    /// scrolls a newly selected node into view automatically.</summary>
    private void TrackNewNode(TreeNode newNode)
    {
      bool mustSelect = treeView1.SelectedNode == null || treeView1.SelectedNode == Current;
      Current = newNode;
      if (mustSelect) treeView1.SelectedNode = newNode;
    }

    /// <summary>Adds a leaf under the given pass node. Expands the pass node so the new leaf is visible, unless
    /// the user deliberately collapsed it while it already had leaves and is still looking at its summary —
    /// popping it open on every new frame/image would fight that choice.</summary>
    private void AddLeaf(TreeNode passNode, TreeNode leaf)
    {
      bool keepCollapsed = !passNode.IsExpanded && passNode.Nodes.Count > 0 && treeView1.SelectedNode == passNode;
      passNode.Nodes.Add(leaf);
      if (!keepCollapsed) passNode.Expand();
      TrackNewNode(leaf);
    }

    private string BuildFrameText(Frame frame, DecodeSnapshot snapshot)
    {
      string tlm = "";
      var def = TelemetryRegistry?.ForNorad(snapshot.Satellite?.norad_cat_id);
      if (def != null)
      {
        var record = TelemetryParser.Parse(def, frame.Bytes);
        if (record != null)
          tlm = "TELEMETRY:\n" +
            string.Join("", record.Fields.Select(f => $"  {f.Name}: {f.Value}{(f.Units.Length > 0 ? " " + f.Units : "")}\n")) +
            "\n";
      }

      var chars = frame.Ascii;
      string asc = "";
      for (int i = 0; i < chars.Length; i += 28)
        asc += "  " + chars.Substring(i, Math.Min(28, chars.Length - i)) + "\n";
      asc = "ASCII:\n" + asc + "\n";

      var bytes = frame.Bytes;
      string hex = "";
      for (int i = 0; i < bytes.Length; i += 8)
        hex += $"  {i:X3}  " + string.Join(" ", bytes.Skip(i).Take(8).Select(b => b.ToString("X2"))) + "\n";
      hex = "HEX:\n" + hex + "\n";

      string meta = "META:\n" +
        $"  CFO: {frame.CfoHz:F1} Hz\n" +
        $"  SNR: {frame.SnrDb:F1} dB\n" +
        $"  CRC: {frame.CrcValid switch { true => "OK", false => "FAIL", null => "n/a" }}\n" +
        $"  Corrections: {frame.CorrectedBits}\n" +
        $"  Erasures: {frame.ErasedBytes}\n";

      return tlm + asc + hex + meta;
    }




    //----------------------------------------------------------------------------------------------
    //                                      sstv images
    //----------------------------------------------------------------------------------------------
    // called on the decode worker thread (or on the UI thread when a disposed decoder flushes); the
    // finalized image is auto-saved here, before marshaling, so a pass ending with the panel closing
    // cannot lose it
    private void SstvImageHandler(SstvImageEvent evt, DecodeSnapshot snapshot, Dictionary<int, TreeNode> imageNodes)
    {
      string? savedPath = evt.Final && evt.ValidRows > 0 ? SaveImageToFile(evt, snapshot) : null;
      BeginInvoke(() => ShowImage(evt, snapshot, imageNodes, savedPath));
    }

    private void ShowImage(SstvImageEvent evt, DecodeSnapshot snapshot, Dictionary<int, TreeNode> imageNodes, string? savedPath)
    {
      var txPassInfo = EnsureCurrentPassNode(snapshot);

      bool isNew = !imageNodes.TryGetValue(evt.ImageId, out TreeNode? node);
      if (isNew)
      {
        node = new TreeNode();
        node.Tag = new SstvImageInfo(snapshot, evt);
        imageNodes[evt.ImageId] = node;
        txPassInfo.ImageCount++;
        AddLeaf(CurrentPassNode!, node);
      }

      // swap in the new reconstruction; dispose the previous bitmap only after the PictureBox lets go of it
      var info = (SstvImageInfo)node!.Tag;
      var oldBitmap = info.Bitmap;
      info.Event = evt;
      info.Bitmap = evt.Image.ToBitmap();
      if (savedPath != null) info.SavedPath = savedPath;
      node.Text = $"{info.FirstSeen:HH:mm:ss}  {evt.Mode}  {evt.ValidRows}/{evt.Image.Height} rows";
      if (ImageBox.Image == oldBitmap) ImageBox.Image = info.Bitmap;
      oldBitmap?.Dispose();

      // an accepted image train is real content: un-gray the pass entry the way a valid frame does
      if (!txPassInfo.HasValidFrame)
      {
        txPassInfo.HasValidFrame = true;
        CurrentPassNode!.ForeColor = Color.Empty;
      }

      if (!evt.Final) StatusLabel.Text = "decoding...";

      if (treeView1.SelectedNode == node) DisplayImageInfo(info);
      else if (treeView1.SelectedNode == CurrentPassNode) richTextBox1.Text = txPassInfo.Describe();
    }

    private void DisplayImageInfo(SstvImageInfo info)
    {
      if (richTextBox1.Parent != ImageSplitContainer.Panel2)
      {
        richTextBox1.Parent = ImageSplitContainer.Panel2;
        richTextBox1.Dock = DockStyle.Fill;
      }
      ImageSplitContainer.Visible = true;
      ImageBox.Image = info.Bitmap;
      richTextBox1.Text = info.Describe();
    }

    // switches the right panel back to plain telemetry text, undoing DisplayImageInfo's reparenting of
    // richTextBox1 into the (now hidden) ImageSplitContainer
    private void ShowTelemetryText()
    {
      if (richTextBox1.Parent != splitContainer1.Panel2)
      {
        richTextBox1.Parent = splitContainer1.Panel2;
        richTextBox1.Dock = DockStyle.Fill;
      }
      ImageSplitContainer.Visible = false;
    }

    /// <summary>Auto-save the finalized image as PNG + JSON metadata sidecar under the user data folder.</summary>
    private static string? SaveImageToFile(SstvImageEvent evt, DecodeSnapshot snapshot)
    {
      try
      {
        string folder = Path.Combine(Utils.GetUserDataFolder(), "SstvImages");
        string sat = string.Concat((snapshot.Satellite?.name ?? "Unknown").Split(Path.GetInvalidFileNameChars()));
        string path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{sat}_{evt.Mode}_{evt.ImageId}.png");
        evt.Image.SavePng(path);

        var meta = new
        {
          Utc = DateTime.UtcNow,
          Satellite = snapshot.Satellite?.name,
          Norad = snapshot.Satellite?.norad_cat_id,
          Transmitter = snapshot.Transmitter.description,
          TransmitterUuid = snapshot.Transmitter.uuid,
          Mode = evt.Mode.ToString(),
          evt.FromVis,
          evt.ValidRows,
          evt.Image.Width,
          evt.Image.Height
        };
        File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonConvert.SerializeObject(meta, Formatting.Indented));
        return path;
      }
      catch (Exception e)
      {
        Log.Error(e, "Failed to save SSTV image");
        return null;
      }
    }

    private void SaveImageMNU_Click(object sender, EventArgs e)
    {
      if (treeView1.SelectedNode?.Tag is not SstvImageInfo info) return;
      using var dlg = new SaveFileDialog
      {
        Filter = "PNG Image|*.png",
        FileName = $"{info.FirstSeen:yyyyMMdd_HHmmss}_{info.Event.Mode}.png"
      };
      if (dlg.ShowDialog() == DialogResult.OK) info.Event.Image.SavePng(dlg.FileName);
    }

    private void CopyImageMNU_Click(object sender, EventArgs e)
    {
      if (ImageBox.Image != null) Clipboard.SetImage(ImageBox.Image);
    }




    //----------------------------------------------------------------------------------------------
    //                                       save to file
    //----------------------------------------------------------------------------------------------
    // mirror everything shown in the tree to the file: the date and time, satellite, transmitter
    // and frame length in the header, the frame address (if any), then the frame detail shown in the
    // right pane
    private void SaveFrameToFile(Frame frame, string addr, string frameText, DecodeSnapshot snapshot)
    {
      if (!ctx.Settings.Telemetry.ArchiveToFile) return;

      FrameLogger ??= CreateFrameLogger();

      string header = $"Sat: {snapshot.Transmitter.Satellite.name}  Tx: \"{snapshot.Transmitter.description}\"  Uuid: {snapshot.Transmitter.uuid}  Frame: {frame.Length} bytes" +
        (addr.Length > 0 ? $"  Addr: {addr}" : "");
      FrameLogger.Information("{Header}\n{Body}", header, frameText);
    }

    private static ILogger CreateFrameLogger()
    {
      string fileName = Path.Combine(Utils.GetUserDataFolder(), "TelemetryDecodes", "frames_.txt");
      return new LoggerConfiguration()
        .WriteTo.File(fileName,
          rollingInterval: RollingInterval.Day,
          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss}  {Message:lj}{NewLine}",
          shared: true)
        .CreateLogger();
    }

    internal void UpdateTxStatus()
    {
      SatAboveHorizon = ctx.SdrPasses.GetNextPass(Satellite)?.IsAboveHorizon() ?? false;
      CreatDestroyPipeline();

      if (Terrestrial) StatusLabel.Text = "not decoded";
      else if (!IsDecodable()) StatusLabel.Text = "format not supported";
      else if (!SatAboveHorizon) StatusLabel.Text = "satellite below horizon";
      else StatusLabel.Text = "ready to decode";
    }

    internal void ProcessSamples(DataEventArgs<Complex32> e)
    {
      Decoder?.StartProcessing(e);
    }

    private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
    {
      var node = e.Node;
      if (node == null) return;

      if (node.Tag is SstvImageInfo imageInfo)
      {
        DisplayImageInfo(imageInfo);
        return;
      }

      ShowTelemetryText();
      if (node.Level == 0)
      {
        var info = node.Tag as TxPassInfo;
        richTextBox1.Text = info!.Describe();
      }
      else
        richTextBox1.Text = (string)node!.Tag!;
    }

    private void ClearAllMNU_Click(object sender, EventArgs e)
    {
      Current = null;
      ShowTelemetryText();
      ImageBox.Image = null;
      richTextBox1.Clear();
      treeView1.Nodes.Clear();
    }
  }
}