# Stats

Stats is a framework context for defining, grouping, modifying, and evaluating runtime numeric stats. The core context is engine-agnostic and does not depend on Unity APIs.

## Language

**Stat**:
A named numeric property whose value can be defined, modified, and queried within a stat system.
_Avoid_: Attribute, property

**StatSet**:
A collection of stats that belongs to one StatsOwner and retains an immutable reference to it.
_Avoid_: AttributeSet, stat collection

**StatsOwner**:
An object that owns exactly one stat set and a tag set used to classify it. The owner of its stat set cannot change. Objects with only tags are not StatsOwners.
_Avoid_: Attribute owner, stat container

**StatsOwnerGroup**:
A group of stats owners, potentially with different concrete stat set types, that can apply shared modifiers to compatible members.
_Avoid_: StatSetGroup, StatsOwner collection

**StatKey**:
A build-stable identifier for a stat that lets modifiers and other systems refer to that stat without using a raw string.
_Avoid_: StatId, StatDefinition, stat name

**Modifier**:
A change applied to a stat during one of the stat system's fixed calculation stages. Custom modifier operations are not part of the model.
_Avoid_: Stat modifier, attribute modifier

**ModifierSource**:
A stable identity that groups modifier registrations produced by the same gameplay source, including registrations on different stats owners, so they can be removed together.
_Avoid_: Modifier owner, source object

**ModifierHandle**:
A removable registration of one modifier on one target. It is not the modifier definition itself.
_Avoid_: Modifier reference, modifier token

**DynamicModifierValue**:
A modifier value derived from one to three explicitly declared value inputs. Input changes cause dependent stats to be recalculated, and final-value dependencies must remain acyclic.
_Avoid_: Reactive modifier, calculated modifier

**ValueInput**:
An observable numeric input used by a dynamic modifier value. It can expose a stat's base value, a stat's final value, or a changing value supplied by another gameplay context.
_Avoid_: ModifierSource, value provider

**BaseValue**:
The unmodified value of a stat before modifiers are applied.
_Avoid_: Initial value, raw value

**FinalValue**:
The calculated value of a stat after all relevant modifiers are applied.
_Avoid_: Current value, result value

**RoundingRule**:
An optional rule that gives a stat integer semantics by rounding its calculated value before it becomes the final value.
_Avoid_: Integer stat, rounding modifier

**RangeStat**:
A stat whose value is an interval between a minimum and maximum possible value. It does not represent a current value constrained by a maximum value.
_Avoid_: Bounded stat, current/max stat

**BaseRange**:
The unmodified interval of a range stat.
_Avoid_: Initial range, raw range

**FinalRange**:
The calculated interval of a range stat after all relevant modifiers are applied.
_Avoid_: Current range, result range
