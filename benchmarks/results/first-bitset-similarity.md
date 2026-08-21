# First bitset similarity experiment

Date: August 21, 2026  
BenchmarkDotNet: 0.15.8, ShortRun (three warmups and three measured iterations)  
Runtime: .NET 10.0.9, X64 RyuJIT x86-64-v4  
Machine: AMD Ryzen 7 8845HS, 8 physical cores, Windows 11  
Available intrinsics: AVX2, AVX-512, VPOPCNTDQ, POPCNT

This experiment compares two typed multi-hot path vectors and computes XOR
difference, AND intersection, OR union, normalized Hamming, Jaccard, and binary
cosine in one pass. Every candidate was validated against the scalar oracle
before measurement.

| Feature width | Density | Scalar oracle | Manual unroll | Vector256 candidate |
| ---: | ---: | ---: | ---: | ---: |
| 256 bits | 5% | 9.51 ns | 11.47 ns | 13.31 ns |
| 256 bits | 25% | 7.60 ns | 14.09 ns | 13.30 ns |
| 4,096 bits | 5% | 66.03 ns | 132.64 ns | 140.84 ns |
| 4,096 bits | 25% | 74.33 ns | 123.74 ns | 153.48 ns |
| 16,384 bits | 5% | 209.15 ns | 486.13 ns | 526.56 ns |
| 16,384 bits | 25% | 201.78 ns | 481.52 ns | 500.54 ns |

All measured kernels allocated **0 bytes per comparison**.

## Decision

The scalar `BitOperations.PopCount` loop becomes the first production kernel.
Manual four-word unrolling was 1.2-2.4 times slower, and the Vector256 candidate
was 1.4-2.5 times slower. Its bitwise operations are wide, but extracting lanes
for scalar population-count reduction erases the benefit on this runtime and
processor.

This does not establish a universal scalar advantage. The SIMD track remains a
benchmark candidate for native vector-popcount reductions, batch-oriented
comparison, other CPU architectures, and future runtime changes. Dispatch will
be introduced only after a candidate wins across a declared width threshold and
preserves exact results.

The authoritative raw BenchmarkDotNet artifacts are intentionally generated
locally and can be reproduced with:

```shell
dotnet run --project benchmarks/Nodal.PatternRecognition.Benchmarks -c Release
```
