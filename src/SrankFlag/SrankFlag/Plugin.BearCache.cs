using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SrankFlag;

public sealed partial class Plugin
{
    private bool MainSiteWorld(string world)=>srankData?.dcs.Any(dc=>BearRefreshPolicy.UsesMainSite(dc.dc)&&dc.worlds.Contains(world,StringComparer.Ordinal))==true;
    private IEnumerable<SnapshotData> AllSnapshots()=>srankData?.dcs is {Count:>0} all?all:srankData?.data is {} selected?[selected]:[];

    private void SaveBearCache()
    {
        try
        {
            var kills=new Dictionary<string,double>(StringComparer.Ordinal);
            var snapshots=new Dictionary<string,double>(StringComparer.Ordinal);
            foreach(var dc in AllSnapshots())
            {
                snapshots[dc.dc]=dc.snapshot;
                foreach(var hunt in dc.hunts)
                    foreach(var world in hunt.worlds)
                        if(double.IsFinite(world.lastDeath)&&world.lastDeath>=0)kills[hunt.en+"|"+world.world]=world.lastDeath;
            }
            File.WriteAllText(BearCachePath,JsonSerializer.Serialize(new{version=2,snapshots,kills}));
        }
        catch(Exception ex){log.Warning("[SrankFlag] 写缓存失败："+ex.Message);}
    }

    private void MergeBearCache()
    {
        try
        {
            if(!File.Exists(BearCachePath))return;
            using var document=JsonDocument.Parse(File.ReadAllText(BearCachePath));
            var root=document.RootElement;
            bool authoritative=root.TryGetProperty("version",out var version)&&version.ValueKind==JsonValueKind.Number&&version.GetInt32()>=2;
            if(!root.TryGetProperty("kills",out var kills)||kills.ValueKind!=JsonValueKind.Object)return;
            foreach(var dc in AllSnapshots())
            {
                bool matched=false;
                foreach(var hunt in dc.hunts)
                {
                    foreach(var world in hunt.worlds)
                        if(kills.TryGetProperty(hunt.en+"|"+world.world,out var time)&&time.ValueKind==JsonValueKind.Number&&time.TryGetDouble(out double timestamp)&&double.IsFinite(timestamp)&&timestamp>=0)
                        {world.lastDeath=BearRefreshPolicy.ResolveDeath(world.lastDeath,timestamp,0,authoritative);matched=true;}
                    if(hunt.worlds.Length>0)hunt.lastDeath=authoritative?hunt.worlds.Max(world=>world.lastDeath):Math.Max(hunt.lastDeath,hunt.worlds.Max(world=>world.lastDeath));
                }
                if(!matched)continue;
                if(root.TryGetProperty("snapshots",out var snapshots)&&snapshots.ValueKind==JsonValueKind.Object&&snapshots.TryGetProperty(dc.dc,out var snapshot)&&snapshot.TryGetDouble(out double at)&&double.IsFinite(at))dc.snapshot=Math.Max(dc.snapshot,at);
                else if(root.TryGetProperty("snapshot",out var legacy)&&legacy.ValueKind==JsonValueKind.Number&&legacy.TryGetDouble(out double old)&&double.IsFinite(old))dc.snapshot=Math.Max(dc.snapshot,old);
            }
        }
        catch(Exception ex){log.Warning("[SrankFlag] 读缓存失败："+ex.Message);}
    }
}
