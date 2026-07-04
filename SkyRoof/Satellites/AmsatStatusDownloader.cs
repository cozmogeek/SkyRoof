using System.Text.RegularExpressions;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using Serilog;
using VE3NEA;


namespace SkyRoof
{
  public class AmsatStatusLoader
  {
    private readonly string url = "https://www.amsat.org/status/?app=SkyRoof";
    private readonly string[] activeColors = ["#648fff", "#785ef0"];   // blue, purple
    private readonly string[] inactiveColors = ["#ffb000", "#dc267f"]; // yellow, red
    private readonly string[] skipColors = ["C0C0C0", "#fe6100"];     // gray, orange

    public readonly Dictionary<int, bool> Statuses = [];
    public Context ctx;
    private bool busy;

    public async Task<bool> GetStatusesAsync()
    {
      if (!ctx.Settings.Amsat.Enabled || ctx.GroupViewPanel == null) return true;

      // guard against overlapping runs (startup + hourly tick + menu) interleaving their
      // mutations of the shared satellite collection
      if (busy) return true;
      busy = true;
      try
      {
        string html;
        IDocument document;

        // do all awaits up front: while suspended here the message pump (incl. a modal dialog)
        // runs, but no shared state is touched yet
        try
        {
          html = await DownloadStatusesAsync();
          var config = Configuration.Default.WithDefaultLoader();
          var browsingContext = BrowsingContext.New(config);
          document = await browsingContext.OpenAsync(req => req.Content(html));
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error downloading AMSAT status");
          return false;
        }

        // apply synchronously, with no awaits in between, so the message pump cannot interleave
        // these satellite-collection mutations and the virtual-list update with other UI work
        try
        {
          ExtractSatLabels(html);
          ParseAmsatHtml(document);
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error parsing AMSAT status");
          File.WriteAllText(Path.Combine(Utils.GetUserDataFolder(), "Downloads", "amsat_status_error.html"), html);
          return false;
        }

        ctx.GroupViewPanel?.ShowAmsatStatuses();
        return true;
      }
      finally
      {
        busy = false;
      }
    }

    private readonly Regex optionsRx = new Regex(@"(?is)<select\s+name\s*=\s*[""']SatName[""'][^>]*>(?:(?:(?!</select>).)*?\bvalue\s*=\s*[""']([^""']*)[""'])*.*?</select>");
    private readonly Regex tupleRx = new Regex(@"^(.*?)_\[([^\]]*)\]$");
    private void ExtractSatLabels(string html)
    {

      try
      {
        // extract entries
        var m = optionsRx.Match(html);
        if (!m.Success) throw new Exception("Satellite labels not found");
        var entries = m.Groups[1].Captures.Skip(1).Select(c => c.Value).ToArray();
        if (entries.Length == 0) throw new Exception("0 satellite labels found");

        // clear old entries
        foreach (var sat in ctx.SatnogsDb.Satellites) sat.AmsatEntries = [];

        // add amsat names to the sat
        foreach (var entry in entries)
        {
          var name = SatnogsDbSatellite.MakeSearchText(tupleRx.Match(entry).Groups[1].Value);
          var sats = ctx.SatnogsDb.Satellites.Where(s => s.SearchText.Contains($"|{name}|")).ToArray();

          if (sats.Length == 1)
            sats[0].AmsatEntries.Add(entry);
          else
            Log.Warning($"{sats.Length} satellites found for AMSAT label {name}");
        }

        // rebuild AllNames for the sat
        foreach (var sat in ctx.SatnogsDb.Satellites) sat.BuildAllNames();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error extracting satellite labels from AMSAT status HTML");
      }
    }

    private void ParseAmsatHtml(IDocument document)
    {
      var tables = document.QuerySelectorAll("table");
      var rows = tables[2].QuerySelectorAll("tr");

      Statuses.Clear();
      foreach (var row in rows.Skip(1)) ParseRow(row);
    }

    private void ParseRow(IElement row)
    {
      {
        var cells = row.QuerySelectorAll("td").ToArray();
        string satId = cells[0].TextContent;
        var sat = ctx.SatnogsDb.Satellites.FirstOrDefault(s => s.AmsatEntries.Contains(satId));

        if (sat == null)
        {
          Log.Warning($"Parsing AMSAT status error: no norad_id found for {satId}");
          return;
        }

        string? color = cells.Skip(1).Take(24).Select(cell => cell.GetAttribute("bgcolor"))
          .FirstOrDefault(color => !skipColors.Contains(color));

        bool status;
        if (activeColors.Contains(color)) status = true;
        else if (inactiveColors.Contains(color)) status = false;
        else return;

        int norad_id = sat.norad_cat_id!.Value;

        if (Statuses.ContainsKey(norad_id))
          // true if at least one transmitter is active
          Statuses[norad_id] = Statuses[norad_id] || status;
        else
          Statuses[norad_id] = status;
      }
    }

    private async Task<string> DownloadStatusesAsync()
    { 
        using (HttpClient client = new HttpClient())
        {
          // download
          var response = await client.GetAsync(url);
          return await response.Content.ReadAsStringAsync();
        }
    }

    public string? SendAmsatStatus(string satName, string report)
    {
      try
      {
        // build message
        var queryParams = HttpUtility.ParseQueryString("");
        var now = DateTime.UtcNow - TimeSpan.FromMinutes(1); // margin for time error on the server

        queryParams["SatSubmit"] = "yes";
        queryParams["Confirm"] = "yes";
        queryParams["SatName"] = satName;
        queryParams["SatYear"] = $"{now.Year}";
        queryParams["SatMonth"] = $"{now.Month:D2}";
        queryParams["SatDay"] = $"{now.Day:D2}";
        queryParams["SatHour"] = $"{now.Hour:D2}";
        queryParams["SatPeriod"] = $"{now.Minute / 15}";
        queryParams["SatCall"] = $"{ctx.Settings.User.Call.ToUpper()}";
        queryParams["SatReport"] = report;
        queryParams["SatGridSquare"] = $"{ctx.Settings.User.Square.ToUpper()}";
        queryParams["App"] = "SkyRoof";

        // send
        string urlString = $"https://www.amsat.org/status/submit.php?{queryParams}";
        HttpClient client = new();
        var response = client.GetAsync(urlString).Result;

        // log
        var content = response.Content.ReadAsStringAsync().Result;
        content = Utils.HtmlToText(content).Replace("\n", " ");
        content = Regex.Replace(content, @"\s+", " ");
        Log.Information($"AMSAT request: {urlString}");
        Log.Information($"AMSAT reply: {content}");

        if (!response.IsSuccessStatusCode) throw new Exception($"HTTP error {response.StatusCode}");

        return null; // no error
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to send AMSAT status");
        return ex.Message;
      }
    }
  }
}
