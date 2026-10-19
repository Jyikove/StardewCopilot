using Microsoft.Xna.Framework.Content;
using StardewCodexChat.Core;
var game=Environment.GetEnvironmentVariable("GAME_PATH")??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+"/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS";
using var content=new ContentManager(new Services(),Path.GetFullPath(game+"/../Resources/Content"));
var timer=System.Diagnostics.Stopwatch.StartNew();
var terms=new GameTerminology.Snapshot(GameTermSources.Read((asset,chinese)=>content.Load<Dictionary<string,string>>(asset+(chinese?".zh-CN":""))));
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
Check(terms.Count>800,"installed game assets provide broad name coverage without shipped translation files");
foreach(var pair in new[]{("Steel Falchion","钢刀"),("Lava Katana","熔岩武士刀"),("Galaxy Sword","银河剑"),("Infinity Blade","无限之刃"),("Templar's Blade","圣堂之刃"),("Crusader","十字军"),("Speed","速度"),("Defense","防御"),("Crit. Chance","暴击率"),("Crit. Power","暴击力量"),("Damage","伤害")})
 Check(terms.Rewrite(pair.Item1)==pair.Item2,"game-owned Chinese term: "+pair.Item1);
Check(terms.Context("圣堂之刃 Crusader Steel Falchion").Contains("Crusader = 十字军"),"Chinese question and English evidence select official terminology");
Check(terms.Rewrite("Piña Colada")!="Piña Colada","game names with accented Latin letters are covered");
Check(terms.Rewrite("Steel Falchion（钢刀）")=="钢刀","real weapon translation does not leave a duplicate parenthetical name");
Check(terms.Rewrite("Steel Falchion（+4 Speed）")=="钢刀（+4 速度）","real weapon attributes survive duplicate-name cleanup");
string source="https://stardewvalleywiki.com/Steel_Falchion";
Check(terms.Rewrite("Steel Falchion["+source+"]")=="钢刀["+source+"]","actual catalog leaves source URLs byte-for-byte unchanged");
Console.WriteLine($"All {passed} game-terminology checks passed; terms={terms.Count}; loadMs={timer.ElapsedMilliseconds}. No save or model used.");
sealed class Services:IServiceProvider {public object? GetService(Type t)=>null;}
