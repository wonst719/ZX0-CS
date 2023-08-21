/*
 * (c) Copyright 2021 by Einar Saukas. All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * The name of its author may not be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System.Diagnostics;

namespace zx0;

public class Program
{
    public static readonly int MAX_OFFSET_ZX0 = 32640;
    public static readonly int MAX_OFFSET_ZX7 = 2176;
    public static readonly int DEFAULT_THREADS = 4;

    private static int parseInt(string s)
    {
        try
        {
            return int.Parse(s);
        }
        catch (Exception e)
        {
            return -1;
        }
    }

    private static void reverse(byte[] array)
    {
        int i = 0;
        int j = array.Length - 1;
        while (i < j)
        {
            byte k = array[i];
            array[i++] = array[j];
            array[j--] = k;
        }
    }

    private static byte[] zx0(byte[] input, int skip, bool backwardsMode, bool classicMode, bool quickMode, int threads, bool verbose, int[] delta)
    {
        return new Compressor().compress(
                new Optimizer().optimize(input, skip, quickMode ? MAX_OFFSET_ZX7 : MAX_OFFSET_ZX0, threads, verbose),
                input, skip, backwardsMode, !classicMode && !backwardsMode, delta);
    }

    private static byte[] dzx0(byte[] input, bool backwardsMode, bool classicMode)
    {
        return new Decompressor().decompress(input, backwardsMode, !classicMode && !backwardsMode);
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("ZX0 v2.2: Optimal data compressor by Einar Saukas");

        // process optional parameters
        int threads = DEFAULT_THREADS;
        bool forcedMode = false;
        bool classicMode = false;
        bool backwardsMode = false;
        bool quickMode = false;
        bool decompress = false;
        int skip = 0;
        int i = 0;
        while (i < args.Length && (args[i].StartsWith("-") || args[i].StartsWith("+")))
        {
            if (args[i].StartsWith("-p"))
            {
                threads = parseInt(args[i].Substring(2));
                if (threads <= 0)
                {
                    Console.WriteLine("Error: Invalid parameter " + args[i]);
                    Environment.Exit(1);
                }
            }
            else
            {
                switch (args[i])
                {
                    case "-f":
                        forcedMode = true;
                        break;
                    case "-c":
                        classicMode = true;
                        break;
                    case "-b":
                        backwardsMode = true;
                        break;
                    case "-q":
                        quickMode = true;
                        break;
                    case "-d":
                        decompress = true;
                        break;
                    default:
                        skip = parseInt(args[i]);
                        if (skip <= 0)
                        {
                            Console.WriteLine("Error: Invalid parameter " + args[i]);
                            Environment.Exit(1);
                        }
                        break;
                }
            }
            i++;
        }

        if (decompress && skip > 0)
        {
            Console.WriteLine("Error: Decompressing with " + (backwardsMode ? "suffix" : "prefix") + " not supported");
            Environment.Exit(1);
        }

        // determine output filename
        string outputName = null;
        if (args.Length == i + 1)
        {
            if (!decompress)
            {
                outputName = args[i] + ".zx0";
            }
            else
            {
                if (args[i].Length > 4 && args[i].EndsWith(".zx0"))
                {
                    outputName = args[i].Substring(0, args[i].Length - 4);
                }
                else
                {
                    Console.WriteLine("Error: Cannot infer output filename");
                    Environment.Exit(1);
                }
            }
        }
        else if (args.Length == i + 2)
        {
            outputName = args[i + 1];
        }
        else
        {
            Console.WriteLine("Usage: zx0.exe [-pN] [-f] [-c] [-b] [-q] [-d] input [output.zx0]\n" +
                    "  -p      Parallel processing with N threads\n" +
                    "  -f      Force overwrite of output file\n" +
                    "  -c      Classic file format (v1.*)\n" +
                    "  -b      Compress backwards\n" +
                    "  -q      Quick non-optimal compression\n" +
                    "  -d      Decompress");
            Environment.Exit(1);
        }

        // read input file
        byte[] input = null;
        try
        {
            input = File.ReadAllBytes(args[i]);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: Cannot read input file " + args[i]);
            Environment.Exit(1);
        }

        // determine input size
        if (input.Length == 0)
        {
            Console.WriteLine("Error: Empty input file " + args[i]);
            Environment.Exit(1);
        }

        // validate skip against input size
        if (skip >= input.Length)
        {
            Console.WriteLine("Error: Skipping entire input file " + args[i]);
            Environment.Exit(1);
        }

        // check output file
        if (!forcedMode && File.Exists(outputName))
        {
            Console.WriteLine("Error: Already existing output file " + outputName);
            Environment.Exit(1);
        }

        // conditionally reverse input file
        if (backwardsMode)
        {
            reverse(input);
        }

        // generate output file
        byte[] output = null;
        int[] delta = { 0 };

        if (!decompress)
        {
            output = zx0(input, skip, backwardsMode, classicMode, quickMode, threads, true, delta);
        }
        else
        {
            try
            {
                output = dzx0(input, backwardsMode, classicMode);
            }
            catch (IndexOutOfRangeException e)
            {
                Console.WriteLine("Error: Invalid input file " + args[i]);
                Environment.Exit(1);
            }
        }

        // conditionally reverse output file
        if (backwardsMode)
        {
            reverse(output);
        }

        // write output file
        try
        {
            if (!forcedMode)
            {
                if (File.Exists(outputName))
                {
                    throw new Exception("File already exists");
                }
            }
            File.WriteAllBytes(outputName, output);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: Cannot write output file " + outputName);
            Environment.Exit(1);
        }

        // done!
        if (!decompress)
        {
            Console.WriteLine("File " + (skip > 0 ? "partially " : "") + "compressed " + (backwardsMode ? "backwards " : "") + "from " + (input.Length - skip) + " to " + output.Length + " bytes! (delta " + delta[0] + ")");
        }
        else
        {
            Console.WriteLine("File decompressed " + (backwardsMode ? "backwards " : "") + "from " + (input.Length - skip) + " to " + output.Length + " bytes!");
        }
    }
}
