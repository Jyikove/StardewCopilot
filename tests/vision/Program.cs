using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using StardewCodexChat.Core;
using StardewCodexChat.Vision;
using StardewValley;
using System.Runtime.InteropServices;

using var game=new Game1();game.Run();

namespace StardewModdingAPI { public static class Context {public static bool IsWorldReady=true,IsSplitScreen=false;} }
namespace StardewValley {
 public sealed class Options {public float zoomLevel=1,uiScale=1;}
 public sealed class Game1:Game {
  public static Game1 game1=null!;
  public static GraphicsDeviceManager graphics=null!;
  public static Options options=new();
  public RenderTarget2D? uiScreen,screen;
  public bool Buffered;
  public bool ShouldDrawOnBuffer()=>Buffered;
  private bool ran;
  private int passed;
  public Game1(){game1=this;graphics=new(this){PreferredBackBufferWidth=640,PreferredBackBufferHeight=480,IsFullScreen=false};IsMouseVisible=false;}
  [DllImport("libSDL2-2.0.0.dylib")] private static extern void SDL_HideWindow(IntPtr window);
  protected override void Initialize(){SDL_HideWindow(Window.Handle);base.Initialize();}
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
  protected override void Draw(GameTime time){if(ran)return;ran=true;try{Test();}finally{Exit();}}
  private void Test(){
   var device=GraphicsDevice;
   int bw=device.PresentationParameters.BackBufferWidth,bh=device.PresentationParameters.BackBufferHeight;
   using var batch=new SpriteBatch(device);using var white=new Texture2D(device,1,1);white.SetData(new[]{Color.White});
   foreach(var mode in new[]{"backbuffer-ui","buffered-ui","buffered-ui-late","backbuffer","world-target"}){
    Buffered=mode is "buffered-ui" or "buffered-ui-late" or "world-target";options.zoomLevel=Buffered?2:1;options.uiScale=mode.Contains("-ui")?2:1;
    using var world=new RenderTarget2D(device,bw/2,bh/2,false,SurfaceFormat.Color,DepthFormat.None,0,RenderTargetUsage.PreserveContents);
    using var ui=new RenderTarget2D(device,bw/2,bh/2,false,SurfaceFormat.Color,DepthFormat.None,0,RenderTargetUsage.PreserveContents);
    screen=world;uiScreen=mode.Contains("-ui")?ui:null;
    device.SetRenderTarget(Buffered?world:null);device.Clear(new Color(60,90,120));
    if(uiScreen!=null){device.SetRenderTarget(ui);device.Clear(Color.Transparent);}
    batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp);
    batch.Draw(white,new Rectangle(10,20,30,40),Color.Red);
    if(mode=="buffered-ui-late"){batch.End();device.SetRenderTarget(world);batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp);}
    var service=new GameScreenCapture();
    var pending=service.CaptureAsync(CancellationToken.None);
    Check(!pending.IsCompleted,"capture waits for render instead of reading the GPU from a worker: "+mode);
    service.BeforeOverlay(batch);
    batch.Draw(white,new Rectangle(0,0,100,100),Color.Yellow);batch.End();
    var frame=pending.GetAwaiter().GetResult();frame.Validate();
    using var preview=SKBitmap.Decode(new ScreenImageProcessor().Preview(frame).Png);
    int scale=mode=="backbuffer"?1:2;
    Check(frame.Width==bw&&frame.Height==bh,"capture uses game backbuffer dimensions: "+mode);
    SKColor Pixel(int x,int y){int i=(y*frame.Width+x)*4;return new(frame.Rgba[i],frame.Rgba[i+1],frame.Rgba[i+2],frame.Rgba[i+3]);}
    var parent=Pixel(20*scale,30*scale);var background=Pixel(bw-30,bh-30);
    Check(parent.Red>240&&parent.Green<10&&parent.Blue<10,"underlying menu is visible but the subsequently drawn assistant is excluded: "+mode);
    Console.WriteLine($"GPU fixture {mode}: {bw}x{bh}, background={background}, parent={parent}");
    Check(background.Red==60&&background.Green==90&&background.Blue==120,"world and UI layers compose without black/transparent background: "+mode);
    if(uiScreen!=null&&mode!="buffered-ui-late"){var pixels=new byte[(bw/2)*(bh/2)*4];ui.GetData(pixels);Check(pixels[(30*(bw/2)+20)*4+1]>240,"GPU readback preserves the active render target for the assistant: "+mode);}
    device.SetRenderTarget(null);uiScreen=screen=null;
   }
   var bytes=new byte[2400*1200*4];for(int y=0;y<1200;y++)for(int x=0;x<2400;x++){int i=(y*2400+x)*4;bytes[i]=(byte)((x+y)%2==0?255:0);bytes[i+1]=(byte)((x+y)%2==0?0:255);bytes[i+3]=255;}
   var original=new ScreenFrame(2400,1200,bytes,DateTimeOffset.UtcNow);var processor=new ScreenImageProcessor();
   var tiny=processor.Preview(original);var crop=processor.Crop(original,new(1100,500,100,100));
   using var decoded=SKBitmap.Decode(crop.Png);
   Check(tiny.Width==960&&tiny.Height==480,"preview reduces dimensions with the original aspect ratio");
   Check(decoded.Width==100&&decoded.Height==100&&decoded.GetPixel(0,0).Red==255&&decoded.GetPixel(1,0).Green==255,"local crop retains original one-pixel detail lost by the smaller preview");
   Check(original.Rgba.SequenceEqual(bytes)&&crop.Label.Contains("1100,500"),"original pixels stay intact and crop metadata gives the actual source rectangle");
   Console.WriteLine($"All {passed} real GPU and image checks passed. Synthetic game layers only; no desktop or save captured.");
  }
 }
}
