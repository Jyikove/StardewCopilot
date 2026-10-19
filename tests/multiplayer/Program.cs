using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Netcode;
using StardewValley;
using StardewValley.Inventories;
using StardewCodexChat.State;
using StardewCodexChat.Core;
using StardewModdingAPI;

int passed=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
// Use real game containers/NetFields, but never create a window, load a save or start a server.
// Skip Farmer's graphics-dependent constructor; initialize its net-field containers for fixtures.
T Bare<T>() where T:class
{
    var value=(T)FormatterServices.GetUninitializedObject(typeof(T));
    for(Type? type=typeof(T);type!=null;type=type.BaseType)
        foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            if(field.FieldType.Namespace=="Netcode"&&field.FieldType.GetConstructor(Type.EmptyTypes)!=null)
                field.SetValue(value,Activator.CreateInstance(field.FieldType));
    return value;
}
Game1.game1=Bare<Game1>();
var farm=new FixtureLocation();farm.name.Value="Farm";
var remote=new FixtureLocation();remote.name.Value="UnsynchronizedFixture";
Game1.game1._locations=new List<GameLocation>{farm,remote};
Game1.game1.instanceGameLocation=farm;
Game1.multiplayerMode=2 /* hosting */;
Game1.year=1;Game1.currentSeason="summer";Game1.dayOfMonth=5;Game1.timeOfDay=900;
Context.IsWorldReady=true;Context.IsMainPlayer=true;
using(var host=JsonDocument.Parse(GameStateReader.Read("get_crops","{\"location\":\"UnsynchronizedFixture\"}")))
    Check(!host.RootElement.GetProperty("data").TryGetProperty("error",out _),"host may inspect loaded locations using the real game location API");
Context.IsMainPlayer=false;Game1.multiplayerMode=1 /* farmhand */;
Check(!remote.IsActiveLocation(),"fixture remote area is actually unsynchronized according to the game API");
foreach(string query in new[]{"get_crops","get_machines"})
{
    using var unavailable=JsonDocument.Parse(GameStateReader.Read(query,"{\"location\":\"UnsynchronizedFixture\"}"));
    Check(unavailable.RootElement.GetProperty("data").GetProperty("error").GetString()!.Contains("synchronized"),"farmhand cannot mistake stale remote data for an empty area: "+query);
    Check(unavailable.RootElement.GetProperty("scope").GetString()!.Contains("not the host"),"farmhand observation identifies the local player and synchronization limits: "+query);
    using var current=JsonDocument.Parse(GameStateReader.Read(query,"{\"location\":\"Farm\"}"));
    Check(!current.RootElement.GetProperty("data").TryGetProperty("error",out _),"farmhand can inspect their actual current location: "+query);
}
var hostFarmer=Bare<Farmer>();var farmhand=Bare<Farmer>();
foreach(var (farmer,count) in new[]{(hostFarmer,2),(farmhand,7)})
{
    farmer.netItems.Value=new Inventory();
    farmer.Items.Add(new FixtureItem{Stack=count});
}
var playerField=typeof(Game1).GetField("_player",BindingFlags.Static|BindingFlags.NonPublic)!;
foreach(var (farmer,count) in new[]{(hostFarmer,2),(farmhand,7),(hostFarmer,2)})
{
    playerField.SetValue(null,farmer);
    using var inventory=JsonDocument.Parse(GameStateReader.Read("get_inventory","{}"));
    Check(inventory.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("count").GetInt32()==count,
        "inventory query reads the currently controlled farmer, never the previously queried farmer");
}
Context.IsWorldReady=false;
using(var unloaded=JsonDocument.Parse(GameStateReader.Read("get_inventory","{}")))
    Check(!unloaded.RootElement.GetProperty("worldReady").GetBoolean(),"disconnected client cannot keep reporting the previous player's inventory");
Console.WriteLine($"All {passed} real-game multiplayer-state checks passed. Synthetic state; no actual peer connection, save, key or network used.");

sealed class FixtureLocation:GameLocation { public override string GetDisplayName()=>Name; }

sealed class FixtureItem:StardewValley.Object
{
    public FixtureItem(){ItemId="378";}
    public override string DisplayName =>"合成矿石";
    public override string Name {get=>"Fixture Ore";set{}}
}
namespace StardewModdingAPI
{
    public static class Context {public static bool IsWorldReady,IsMainPlayer;public static bool IsMultiplayer=>true;}
}
