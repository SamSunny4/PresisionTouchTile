using System;
using System.Reflection;
using Vortice.Direct3D9;

class Program {
    static void Main() {
        foreach (var m in typeof(IDirect3D9Ex).GetMethods()) {
            if (m.Name.Contains("CreateDevice")) {
                Console.Write(m.Name + "(");
                var p = m.GetParameters();
                for (int i=0; i<p.Length; i++) {
                    Console.Write((i>0?", ":"") + (p[i].IsOut ? "out " : "") + p[i].ParameterType.Name + " " + p[i].Name);
                }
                Console.WriteLine(") -> " + m.ReturnType.Name);
            }
        }
        foreach (var m in typeof(IDirect3DDevice9Ex).GetMethods()) {
            if (m.Name.Contains("CreateTexture")) {
                Console.Write(m.Name + "(");
                var p = m.GetParameters();
                for (int i=0; i<p.Length; i++) {
                    Console.Write((i>0?", ":"") + (p[i].IsOut ? "out " : "") + p[i].ParameterType.Name + " " + p[i].Name);
                }
                Console.WriteLine(") -> " + m.ReturnType.Name);
            }
        }
    }
}
