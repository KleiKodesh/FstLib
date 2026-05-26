# Why the Struct Optimization Failed

## The Problem

Converting `MutableArc` from a **class** to a **struct** broke the code because of how C# handles struct modifications through properties.

### The Problematic Code

```csharp
// In FstBuilder.cs, line 79:
_frontier[prefixLen].LastArc.Output = output - _nodeOutput[prefixLen];
```

### Why This Fails with Structs

When `MutableArc` is a **struct** stored in a `List<MutableArc>`:

```csharp
// What the code tries to do:
public MutableArc LastArc => Arcs[Arcs.Count - 1];  // Returns a COPY of the struct
_frontier[prefixLen].LastArc.Output = value;       // Modifies the COPY, not the original!
```

**The issue**: `LastArc` returns a **copy** of the struct (value type semantics), not a reference. Modifying the copy doesn't affect the struct stored in the array.

### With Classes (Original)

```csharp
public MutableArc LastArc => Arcs[Arcs.Count - 1];  // Returns a reference to the object
_frontier[prefixLen].LastArc.Output = value;       // Modifies the actual object ✅
```

Classes are reference types, so the modification works correctly.

---

## Why We Couldn't Just Use a Setter

We tried to work around this with a setter method:

```csharp
public void SetLastArcOutput(long output)
{
    Arcs[ArcCount - 1].Output = output;  // Still doesn't work!
}
```

**This still fails** because:
1. `Arcs[ArcCount - 1]` returns a copy of the struct
2. We modify the copy
3. The copy is discarded
4. The original struct in the array is unchanged

---

## The Correct Solution (If We Wanted Structs)

To make structs work, we'd need to use `ref`:

```csharp
public ref MutableArc LastArcRef => ref Arcs[Arcs.Count - 1];

// Then usage:
ref MutableArc arc = ref _frontier[prefixLen].LastArcRef;
arc.Output = value;  // Now modifies the actual struct ✅
```

**But this has problems:**
1. `ref` returns are complex and error-prone
2. They can't be stored in fields or returned from properties easily
3. They break with LINQ and many other patterns
4. The code would become much harder to maintain

---

## Why Structs Seemed Like a Good Idea

The optimization looked promising because:
- **Fewer allocations**: Structs are stack-allocated, not heap-allocated
- **Better cache locality**: Stack memory is typically more cache-friendly
- **GC pressure reduction**: No garbage collection needed for structs

### The Reality

For `MutableArc`, the benefits don't materialize because:
1. **Frequent modifications**: The code modifies arc fields after creation
2. **Stored in collections**: Structs in `List<T>` get copied on access
3. **Passed by value**: Each access creates a copy, negating allocation savings
4. **Complex semantics**: The modification pattern requires reference semantics

---

## Performance Analysis

### Why Structs Would Actually Be Slower

```csharp
// With struct in List<T>:
var arc = Arcs[i];           // Copy 1: Read from list
arc.Output = value;          // Modify the copy
Arcs[i] = arc;               // Copy 2: Write back to list
// Total: 2 copies per modification

// With class in List<T>:
var arc = Arcs[i];           // Get reference
arc.Output = value;          // Modify in-place
// Total: 0 copies, just reference
```

For a 500k-word dictionary with millions of arc modifications during building, structs would actually **increase** memory traffic and **decrease** performance.

---

## Why the Tests Failed

The tests failed because:
1. Arcs were created with `Target = -2`
2. Later, `LastArc.Output` was modified
3. With structs, the modification didn't persist
4. The FST was built with incorrect arc data
5. Lookups returned wrong results

---

## Lessons Learned

### When Structs Work Well
- ✅ Small, immutable data (like `Point`, `Color`)
- ✅ Value semantics are desired (like `DateTime`)
- ✅ Rarely modified after creation
- ✅ Not stored in collections that require modification

### When Structs Don't Work
- ❌ Mutable data with frequent modifications
- ❌ Stored in collections and modified through accessors
- ❌ Need reference semantics for correctness
- ❌ Performance-critical code where copying is expensive

### For MutableArc
The name itself (`Mutable`) indicates this should be a **class**, not a struct. The mutation pattern is fundamental to how it's used.

---

## Alternative Approaches (Not Pursued)

### 1. Array-Based Storage with Indices
```csharp
public struct MutableArc { ... }
public MutableArc[] Arcs;  // Direct array, not List<T>
public int ArcCount;

// Access:
Arcs[ArcCount - 1].Output = value;  // Still doesn't work!
```
**Problem**: Same issue - struct copy on access.

### 2. Ref Struct (C# 7.2+)
```csharp
public ref struct MutableArc { ... }
```
**Problem**: Can't be stored in `List<T>`, only on stack.

### 3. Unmanaged Struct with Pinning
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct MutableArc { ... }
```
**Problem**: Doesn't solve the copy-on-access issue, adds complexity.

### 4. Object Pool
```csharp
private static ObjectPool<MutableArc> _arcPool;
```
**Problem**: Adds complexity, doesn't provide the allocation savings we wanted.

---

## Conclusion

The struct optimization **failed because C# struct semantics are fundamentally incompatible with the modification pattern used in the code**. The code modifies arcs after storing them in a collection, which requires reference semantics that only classes provide.

**The lesson**: Not all "obvious" optimizations work in practice. Understanding language semantics and actual usage patterns is critical before attempting optimizations.

**The right approach**: Keep `MutableArc` as a class. The allocation overhead is acceptable given the complexity of the alternative approaches and the actual performance impact (which would likely be negative due to struct copying).
