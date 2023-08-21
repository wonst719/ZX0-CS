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

namespace zx0;

public class Optimizer {
    public static readonly int INITIAL_OFFSET = 1;
    public static readonly int MAX_SCALE = 50;

    private Block[] lastLiteral;
    private Block[] lastMatch;
    private Block[] optimal;
    private int[] matchLength;
    private int[] bestLength;

    private static int offsetCeiling(int index, int offsetLimit) {
        return Math.Min(Math.Max(index, INITIAL_OFFSET), offsetLimit);
    }

    private static int eliasGammaBits(int value) {
        int bits = 1;
        while (value > 1) {
            bits += 2;
            value >>= 1;
        }
        return bits;
    }

    public Block optimize(byte[] input, int skip, int offsetLimit, int threads, bool verbose) {

        // allocate all main data structures at once
        int arraySize = offsetCeiling(input.Length-1, offsetLimit)+1;
        lastLiteral = new Block[arraySize];
        lastMatch = new Block[arraySize];
        optimal = new Block[input.Length];
        matchLength = new int[arraySize];
        bestLength = new int[input.Length];
        if (bestLength.Length > 2) {
            bestLength[2] = 2;
        }

        // start with fake block
        lastMatch[INITIAL_OFFSET] = new Block(-1, skip-1, INITIAL_OFFSET, null);

        int dots = 2;
        if (verbose) {
            Console.Write("[");
        }

        // process remaining bytes
        for (int index = skip; index < input.Length; index++) {
            int maxOffset = offsetCeiling(index, offsetLimit);
            if (threads <= 1) {
                optimal[index] = processTask(1, maxOffset, index, skip, input);
            } else {
                int taskSize = maxOffset/threads+1;
                var tasks = new List<Task<Block>>();
                for (int initialOffset = 1; initialOffset <= maxOffset; initialOffset += taskSize) {
                    int finalOffset = Math.Min(initialOffset+taskSize-1, maxOffset);
                    int initialOffset0 = initialOffset;
                    int index0 = index;
                    tasks.Add(Task.Run(() => processTask(initialOffset0, finalOffset, index0, skip, input)));
                }
                foreach (Task<Block> task in tasks) {
                    try {
                        task.Wait();
                        Block taskBlock = task.Result;
                        if (taskBlock != null) {
                            if (optimal[index] == null || optimal[index].getBits() > taskBlock.getBits()) {
                                optimal[index] = taskBlock;
                            }
                        }
                    } catch (Exception e) {
                        throw;
                    }
                }
            }

            // indicate progress
            if (verbose && index*MAX_SCALE/input.Length > dots) {
                Console.Write(".");
                dots++;
            }
        }

        if (verbose) {
            Console.WriteLine("]");
        }

        return optimal[input.Length-1];
    }

    private Block processTask(int initialOffset, int finalOffset, int index, int skip, byte[] input) {
        int bestLengthSize = 2;
        Block optimalBlock = null;
        for (int offset = initialOffset; offset <= finalOffset; offset++) {
            if (index != skip && index >= offset && input[index] == input[index-offset]) {
                // copy from last offset
                if (lastLiteral[offset] != null) {
                    int length = index-lastLiteral[offset].getIndex();
                    int bits = lastLiteral[offset].getBits() + 1 + eliasGammaBits(length);
                    lastMatch[offset] = new Block(bits, index, offset, lastLiteral[offset]);
                    if (optimalBlock == null || optimalBlock.getBits() > bits) {
                        optimalBlock = lastMatch[offset];
                    }
                }
                // copy from new offset
                if (++matchLength[offset] > 1) {
                    if (bestLengthSize < matchLength[offset]) {
                        int bits1 = optimal[index-bestLength[bestLengthSize]].getBits() + eliasGammaBits(bestLength[bestLengthSize]-1);
                        do {
                            bestLengthSize++;
                            int bits2 = optimal[index-bestLengthSize].getBits() + eliasGammaBits(bestLengthSize-1);
                            if (bits2 <= bits1) {
                                bestLength[bestLengthSize] = bestLengthSize;
                                bits1 = bits2;
                            } else {
                                bestLength[bestLengthSize] = bestLength[bestLengthSize-1];
                            }
                        } while(bestLengthSize < matchLength[offset]);
                    }
                    int length = bestLength[matchLength[offset]];
                    int bits = optimal[index-length].getBits() + 8 + eliasGammaBits((offset-1)/128+1) + eliasGammaBits(length-1);
                    if (lastMatch[offset] == null || lastMatch[offset].getIndex() != index || lastMatch[offset].getBits() > bits) {
                        lastMatch[offset] = new Block(bits, index, offset, optimal[index-length]);
                        if (optimalBlock == null || optimalBlock.getBits() > bits) {
                            optimalBlock = lastMatch[offset];
                        }
                    }
                }
            } else {
                // copy literals
                matchLength[offset] = 0;
                if (lastMatch[offset] != null) {
                    int length = index-lastMatch[offset].getIndex();
                    int bits = lastMatch[offset].getBits() + 1 + eliasGammaBits(length) + length*8;
                    lastLiteral[offset] = new Block(bits, index, 0, lastMatch[offset]);
                    if (optimalBlock == null || optimalBlock.getBits() > bits) {
                        optimalBlock = lastLiteral[offset];
                    }
                }
            }
        }
        return optimalBlock;
    }
}
