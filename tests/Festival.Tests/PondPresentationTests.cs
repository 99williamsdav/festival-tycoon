using Festival.Game;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class PondPresentationTests
{
    [TestMethod]
    public void PauseTerminalAndHiddenTimeDoNotEnterTheCosmeticClock()
    {
        var clock=new PondClock();clock.Advance(.02,false,false,false);
        var before=clock.Seconds;var pose=PondPaths.Drake.Sample(before);
        foreach(var gate in new[]{(true,false,false),(false,true,false),(false,false,true)})
        {
            clock.Advance(600,gate.Item1,gate.Item2,gate.Item3);
            Assert.AreEqual(before,clock.Seconds);Assert.AreEqual(pose,PondPaths.Drake.Sample(clock.Seconds));
        }
        clock.Advance(.02,false,false,false);Assert.AreEqual(before+.02,clock.Seconds,1e-12);
        clock.Advance(double.NaN,false,false,false);Assert.AreEqual(before+.02,clock.Seconds,1e-12);
    }

    [TestMethod]
    public void EntireDuckBodiesClearDeliveredShorePadsAndEachOtherDuringTwentyMinutes()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"ROGUELIKE_DESIGN.md")))root=root.Parent;
        Assert.IsNotNull(root);
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,"assets/source/environment/pond-polish-asset-v1/manifest.json")));
        var m=document.RootElement;
        var polygon=m.GetProperty("water_polygon_local_xz").EnumerateArray().Select(p=>new PondPoint(p[0].GetDouble(),p[1].GetDouble())).ToArray();
        var pads=m.GetProperty("lily_pads_local_xzr").EnumerateArray().Select(p=>(X:p[0].GetDouble(),Z:p[1].GetDouble(),R:p[2].GetDouble())).ToArray();
        var radius=m.GetProperty("duck_recommended_clearance_radius").GetDouble();
        using var verification=JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,"assets/source/environment/pond-polish-asset-v1/verification.json")));
        var safe=verification.RootElement.GetProperty("safe_duck_root_polygon_local_xz").EnumerateArray().Select(p=>new PondPoint(p[0].GetDouble(),p[1].GetDouble())).ToArray();
        static bool Inside(PondPoint p,PondPoint[] polygon)
        {
            var inside=false;
            for(var j=0;j<polygon.Length;j++)
            {
                var a=polygon[j];var b=polygon[(j+1)%polygon.Length];
                if((a.Z>p.Z)!=(b.Z>p.Z)&&p.X<(b.X-a.X)*(p.Z-a.Z)/(b.Z-a.Z)+a.X)inside=!inside;
            }
            return inside;
        }
        static double Distance(PondPoint p,PondPoint a,PondPoint b)
        {
            var dx=b.X-a.X;var dz=b.Z-a.Z;var u=Math.Clamp(((p.X-a.X)*dx+(p.Z-a.Z)*dz)/(dx*dx+dz*dz),0,1);
            return Math.Sqrt(Math.Pow(p.X-a.X-u*dx,2)+Math.Pow(p.Z-a.Z-u*dz,2));
        }
        PondDuckPose? lastA=null,lastB=null;
        for(var i=0;i<=12000;i++)
        {
            var time=i*.1;var a=PondPaths.Drake.Sample(time);var b=PondPaths.Brown.Sample(time);
            Assert.IsTrue(Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2))>radius*2+.1);
            foreach(var pose in new[]{a,b})
            {
                var p=new PondPoint(pose.X,pose.Z);var inside=false;
                Assert.IsTrue(Inside(p,safe),"Curved route leaves delivered safe swimming polygon");
                for(var j=0;j<polygon.Length;j++)
                {
                    var v=polygon[j];var w=polygon[(j+1)%polygon.Length];
                    if((v.Z>p.Z)!=(w.Z>p.Z)&&p.X<(w.X-v.X)*(p.Z-v.Z)/(w.Z-v.Z)+v.X)inside=!inside;
                    Assert.IsTrue(Distance(p,v,w)>radius+.1);
                }
                Assert.IsTrue(inside);
                foreach(var pad in pads)Assert.IsTrue(Math.Sqrt(Math.Pow(p.X-pad.X,2)+Math.Pow(p.Z-pad.Z,2))>radius+pad.R+.08);
                Assert.IsTrue(Math.Abs(pose.Bob)<=.00301&&pose.Speed<.15);
            }
            foreach(var pair in new[]{(a,lastA),(b,lastB)})if(pair.Item2 is{} previous)
            {
                var angle=Math.Abs(Math.IEEERemainder(pair.Item1.Yaw-previous.Yaw,Math.Tau));
                Assert.IsTrue(angle<.04,"Turn exceeds 23 degrees/second");
            }
            lastA=a;lastB=b;
        }
    }
}
