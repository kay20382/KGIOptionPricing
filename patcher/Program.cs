using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace patcher
{
    class Program
    {
        static void Main(string[] args)
        {
            string libDir = @"E:\PROG\C#\KGIOptionPricing\KGIOptionPricing\libs\";
            string[] dlls = { 
                Path.Combine(libDir, "TradeCom.dll"),
                Path.Combine(libDir, "QuoteCom.dll"),
                Path.Combine(libDir, "PushClient.dll"),
                Path.Combine(libDir, "Package.dll")
            };

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(libDir);
            
            var parameters = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false };

            foreach (var dllPath in dlls)
            {
                if (!File.Exists(dllPath)) 
                {
                    Console.WriteLine($"DLL not found: {dllPath}");
                    continue;
                }

                try
                {
                    Console.WriteLine($"\n--- Patching {dllPath} ---");
                    
                    string backupPath = dllPath + ".bak3";
                    if (!File.Exists(backupPath))
                    {
                        File.Copy(dllPath, backupPath);
                    }

                    bool patched = false;
                    using (var module = ModuleDefinition.ReadModule(backupPath, parameters))
                    {
                        foreach (var type in module.Types)
                        {
                            var finalizer = type.Methods.FirstOrDefault(m => m.Name == "Finalize");
                            if (finalizer != null)
                            {
                                var processor = finalizer.Body.GetILProcessor();
                                finalizer.Body.Instructions.Clear();
                                finalizer.Body.ExceptionHandlers.Clear();
                                processor.Emit(OpCodes.Ret);
                                patched = true;
                            }

                            var writerLog = type.Methods.FirstOrDefault(m => m.Name == "WriterLog");
                            if (writerLog != null)
                            {
                                var processor = writerLog.Body.GetILProcessor();
                                writerLog.Body.Instructions.Clear();
                                writerLog.Body.ExceptionHandlers.Clear();
                                processor.Emit(OpCodes.Ret);
                                patched = true;
                            }

                            foreach (var method in type.Methods)
                            {
                                if (!method.HasBody) continue;

                                var processor = method.Body.GetILProcessor();
                                var instructions = method.Body.Instructions.ToList();

                                foreach (var instr in instructions)
                                {
                                    if (instr.OpCode == OpCodes.Callvirt || instr.OpCode == OpCodes.Call)
                                    {
                                        if (instr.Operand is MethodReference methodRef)
                                        {
                                            if (methodRef.DeclaringType.FullName == "System.Threading.Thread" && 
                                                methodRef.Name == "Abort")
                                            {
                                                if (methodRef.Parameters.Count == 0)
                                                {
                                                    instr.OpCode = OpCodes.Pop;
                                                    instr.Operand = null;
                                                }
                                                else if (methodRef.Parameters.Count == 1)
                                                {
                                                    instr.OpCode = OpCodes.Pop;
                                                    instr.Operand = null;
                                                    processor.InsertAfter(instr, Instruction.Create(OpCodes.Pop));
                                                }
                                                patched = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (patched)
                        {
                            module.Write(dllPath);
                            Console.WriteLine($"Saved patched DLL to {dllPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error patching {dllPath}: {ex.Message}");
                }
            }
        }
    }
}
