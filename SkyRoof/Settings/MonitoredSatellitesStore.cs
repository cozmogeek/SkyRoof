using Newtonsoft.Json;
using VE3NEA;

namespace SkyRoof
{
  public class MonitoredSatelliteEntry
  {
    public string SatelliteId = "";
    public string? TransmitterId;
    public AutoRecordMode AutoRecordMode = AutoRecordMode.Off;
  }

  public class MonitoredSatelliteList
  {
    public string Id = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public List<MonitoredSatelliteEntry> Entries = new();

    public override string ToString() => Name;
  }

  public class MonitoredSatellitesStore
  {
    public string SelectedListId = "";
    public List<MonitoredSatelliteList> Lists = new();

    public MonitoredSatelliteList? CurrentList =>
      Lists.FirstOrDefault(l => l.Id == SelectedListId) ?? Lists.FirstOrDefault();

    public List<MonitoredSatelliteEntry> CurrentEntries => CurrentList?.Entries ?? new List<MonitoredSatelliteEntry>();

    public IReadOnlyList<string> GetSatelliteIds() =>
      CurrentEntries.Select(e => e.SatelliteId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();

    public bool Contains(string satId) =>
      CurrentEntries.Any(e => e.SatelliteId == satId);

    public MonitoredSatelliteEntry? FindEntry(string satId) =>
      CurrentEntries.FirstOrDefault(e => e.SatelliteId == satId);

    public void AddEntry(SatnogsDbSatellite sat, string? transmitterId = null)
    {
      if (Contains(sat.sat_id)) return;

      transmitterId ??= sat.Transmitters.FirstOrDefault()?.uuid;

      CurrentEntries.Add(new MonitoredSatelliteEntry
      {
        SatelliteId = sat.sat_id,
        TransmitterId = transmitterId,
      });
      SaveToFile();
    }

    public void RemoveEntry(string satId)
    {
      CurrentEntries.RemoveAll(e => e.SatelliteId == satId);
      SaveToFile();
    }

    public void ToggleEntry(SatnogsDbSatellite sat, string? transmitterId = null)
    {
      if (Contains(sat.sat_id))
        RemoveEntry(sat.sat_id);
      else
        AddEntry(sat, transmitterId);
    }

    public void MoveEntry(int fromIndex, int toIndex)
    {
      var entries = CurrentEntries;
      if (fromIndex < 0 || fromIndex >= entries.Count) return;
      toIndex = Math.Max(0, Math.Min(toIndex, entries.Count - 1));
      if (fromIndex == toIndex) return;

      var entry = entries[fromIndex];
      entries.RemoveAt(fromIndex);
      entries.Insert(toIndex, entry);
      SaveToFile();
    }

    public MonitoredSatelliteList CreateList(string name)
    {
      var list = new MonitoredSatelliteList { Name = name.Trim() };
      Lists.Add(list);
      SelectedListId = list.Id;
      SaveToFile();
      return list;
    }

    public MonitoredSatelliteList CloneList(MonitoredSatelliteList source, string name)
    {
      var clone = new MonitoredSatelliteList
      {
        Name = name.Trim(),
        Entries = source.Entries.Select(e => new MonitoredSatelliteEntry
        {
          SatelliteId = e.SatelliteId,
          TransmitterId = e.TransmitterId,
          AutoRecordMode = e.AutoRecordMode,
        }).ToList(),
      };
      Lists.Add(clone);
      SelectedListId = clone.Id;
      SaveToFile();
      return clone;
    }

    public void RenameList(MonitoredSatelliteList list, string name)
    {
      string trimmed = name.Trim();
      if (string.IsNullOrWhiteSpace(trimmed) || trimmed == list.Name) return;
      list.Name = trimmed;
      SaveToFile();
    }

    public void SelectList(string listId)
    {
      if (!Lists.Any(l => l.Id == listId)) return;
      SelectedListId = listId;
      SaveToFile();
    }

    public void ApplyEntryToSelector(Context ctx, MonitoredSatelliteEntry entry)
    {
      var sat = ctx.SatnogsDb?.GetSatellite(entry.SatelliteId);
      if (sat == null) return;

      ctx.SatelliteSelector.SetSelectedSatellite(sat);

      var tx = sat.Transmitters.FirstOrDefault(t => t.uuid == entry.TransmitterId)
        ?? sat.Transmitters.FirstOrDefault();
      if (tx != null)
        ctx.SatelliteSelector.SetSelectedTransmitter(tx);
    }

    public void ApplyMonitoredTransmitterForSelectedSat(Context ctx)
    {
      var sat = ctx.SatelliteSelector.SelectedSatellite;
      if (sat == null) return;

      var entry = FindEntry(sat.sat_id);
      if (entry == null) return;

      var tx = sat.Transmitters.FirstOrDefault(t => t.uuid == entry.TransmitterId)
        ?? sat.Transmitters.FirstOrDefault();
      if (tx == null) return;
      if (ctx.SatelliteSelector.SelectedTransmitter?.uuid == tx.uuid) return;

      ctx.SatelliteSelector.SetSelectedTransmitter(tx);
    }

    public void SyncTransmitterFromSelector(Context ctx, string satId)
    {
      var entry = FindEntry(satId);
      if (entry == null) return;

      string? txId = ctx.SatelliteSelector.SelectedTransmitter?.uuid;
      if (txId == entry.TransmitterId) return;

      entry.TransmitterId = txId;
      SaveToFile();
    }

    private static string GetFileName() =>
      Path.Combine(Utils.GetUserDataFolder(), "MonitoredSatellites.json");

    public void LoadFromFile(Settings settings)
    {
      string path = GetFileName();
      if (File.Exists(path))
      {
        JsonConvert.PopulateObject(File.ReadAllText(path), this);
        Sanitize(null);
        return;
      }

      MigrateFromSettings(settings);
      SaveToFile();
    }

    private void MigrateFromSettings(Settings settings)
    {
      Lists.Clear();
      var list = new MonitoredSatelliteList { Name = "Default" };
      var satSettings = settings.Satellites;

      foreach (var satId in satSettings.MonitoredSatelliteIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
      {
        satSettings.SatelliteCustomizations.TryGetValue(satId, out var cust);
        list.Entries.Add(new MonitoredSatelliteEntry
        {
          SatelliteId = satId,
          TransmitterId = cust?.SelectedTransmitterId,
          AutoRecordMode = cust?.AutoRecordMode ?? AutoRecordMode.Off,
        });
      }

      Lists.Add(list);
      SelectedListId = list.Id;

      // stop re-writing migrated data into Settings.json
      satSettings.MonitoredSatelliteIds.Clear();
    }

    public void SaveToFile()
    {
      File.WriteAllText(GetFileName(), JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    public void Sanitize(SatnogsDb? db)
    {
      Lists.RemoveAll(l => string.IsNullOrWhiteSpace(l.Name));

      if (Lists.Count == 0)
        Lists.Add(new MonitoredSatelliteList { Name = "Default" });

      foreach (var list in Lists)
      {
        list.Entries.RemoveAll(e => string.IsNullOrWhiteSpace(e.SatelliteId));

        if (db != null)
          list.Entries.RemoveAll(e => db.GetSatellite(e.SatelliteId) == null);

        foreach (var entry in list.Entries)
        {
          if (db == null) continue;
          var sat = db.GetSatellite(entry.SatelliteId);
          if (sat == null) continue;

          if (!string.IsNullOrEmpty(entry.TransmitterId) &&
              !sat.Transmitters.Any(t => t.uuid == entry.TransmitterId))
            entry.TransmitterId = sat.Transmitters.FirstOrDefault()?.uuid;
        }

        // dedupe while preserving order
        var seen = new HashSet<string>();
        list.Entries = list.Entries.Where(e => seen.Add(e.SatelliteId)).ToList();
      }

      if (string.IsNullOrEmpty(SelectedListId) || !Lists.Any(l => l.Id == SelectedListId))
        SelectedListId = Lists[0].Id;
    }
  }
}
