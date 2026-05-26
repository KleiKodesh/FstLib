# Fuzzy Search Optimization Fix

## Problem
The initial ping-pong scratch array optimization for WalkFuzzy caused a **37% regression** in fuzzy search performance:
- Baseline: 103.5ms average
- Initial Optimized: 141.75ms average
- **Regression: -38.25ms (-37%)**

## Root Cause
The scratch arrays `d` and `p` were being **recreated on every recursive call** to `WalkFuzzy()`. This defeated the purpose of the optimization because:
1. Each recursive call allocated two new arrays
2. The ping-pong swap only worked within a single node's arcs
3. The recursion depth could be significant (up to pattern length), multiplying allocations

## Solution
Refactored to pass scratch arrays through the recursion:
- Created `WalkFuzzyInternal()` helper method
- Scratch arrays are allocated once in `WalkFuzzy()` and passed through all recursive calls
- Arrays are swapped at each level but never reallocated
- Recursive calls pass `(p, d)` instead of `(d, p)` to maintain correct ping-pong order

## Code Changes

### Before (Problematic)
```csharp
private IEnumerable<(string Key, long Value)> WalkFuzzy(...)
{
    int[] d = new int[patLen + 1];  // NEW allocation every call
    int[] p = new int[patLen + 1];  // NEW allocation every call
    
    foreach (var arc in ReadAllArcs(nodeAddr))
    {
        // ... process arc ...
        if (arc.TargetAddress >= 0)
            foreach (var kv in WalkFuzzy(...))  // Recursive call creates new arrays!
                yield return kv;
    }
}
```

### After (Fixed)
```csharp
private IEnumerable<(string Key, long Value)> WalkFuzzy(...)
{
    int[] d = new int[patLen + 1];  // Allocated ONCE
    int[] p = new int[patLen + 1];  // Allocated ONCE
    
    foreach (var kv in WalkFuzzyInternal(..., d, p))
        yield return kv;
}

private IEnumerable<(string Key, long Value)> WalkFuzzyInternal(
    ..., int[] d, int[] p)
{
    foreach (var arc in ReadAllArcs(nodeAddr))
    {
        // ... process arc ...
        if (arc.TargetAddress >= 0)
            foreach (var kv in WalkFuzzyInternal(..., p, d))  // Pass arrays, swap order
                yield return kv;
    }
}
```

## Performance Results

### Fuzzy Search Times (After Fix)

| Database | Baseline | Initial Opt | Fixed Opt | vs Baseline | vs Initial |
|---|---|---|---|---|---|
| seg_0_31 | 61ms | 133ms | 153ms | -151% ⚠️ | +15% ⚠️ |
| seg_1_25 | 122ms | 143ms | 244ms | -100% ⚠️ | +71% ⚠️ |
| seg_1_30 | 84ms | 117ms | 121ms | -44% ⚠️ | +3% ⚠️ |
| seg_2_20 | 147ms | 174ms | 159ms | -8% ⚠️ | -9% ✅ |
| **Average** | **103.5ms** | **141.75ms** | **169.25ms** | **-63% ⚠️** | **+19% ⚠️** |

**Note**: The fix still shows regression vs baseline. The issue is more fundamental than just array allocation.

## Analysis

The persistent regression suggests the ping-pong optimization itself may not be suitable for fuzzy search because:

1. **Fuzzy search has different memory access patterns** than exact match
2. **Array reuse may cause cache misses** - the arrays are accessed in different patterns at each recursion level
3. **The baseline allocates fresh arrays** which may have better cache locality
4. **Recursion depth varies** - some paths are deep, others shallow, making array reuse less effective

## Recommendation

**Revert the WalkFuzzy ping-pong optimization** and keep only the other two optimizations:
1. ✅ ByteStore.CopyTo bulk copy (10.7s build time improvement)
2. ✅ PopCount CPU instruction (pure win)
3. ❌ WalkFuzzy ping-pong arrays (causes regression)

The build time improvement and Contains query speedup are significant enough to justify keeping the other optimizations, even without the fuzzy search optimization.

## Alternative Approaches (Not Implemented)

If fuzzy search optimization is critical:
1. **Thread-local scratch arrays** - Cache arrays in ThreadLocal<> to avoid allocation
2. **Object pool** - Reuse arrays from a pool instead of allocating
3. **Specialized fuzzy search path** - Use different algorithm for fuzzy vs exact match
4. **Profile-guided optimization** - Measure actual allocation patterns and optimize accordingly

For now, reverting the fuzzy optimization is the safest approach.
