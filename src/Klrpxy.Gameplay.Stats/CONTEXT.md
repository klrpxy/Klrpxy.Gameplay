# Stats

Stats is a framework context for defining, grouping, modifying, and evaluating runtime numeric stats. The core context is engine-agnostic and does not depend on Unity APIs.

## Language

**Stat**:
A named numeric property whose value can be defined, modified, and queried within a stat system.
_Avoid_: Attribute, property

**StatSet**:
A collection of stats that belong to the same owner or calculation context.
_Avoid_: AttributeSet, stat collection

**StatSetGroup**:
A group of stat sets that can hold shared modifiers affecting its members.
_Avoid_: StatSetCollection, stat set list

**StatKey**:
A stable identifier for a stat that lets modifiers and other systems refer to that stat without using a raw string.
_Avoid_: StatId, StatDefinition, stat name

**Modifier**:
A change applied to a stat during value calculation.
_Avoid_: Stat modifier, attribute modifier

**BaseValue**:
The unmodified value of a stat before modifiers are applied.
_Avoid_: Initial value, raw value

**FinalValue**:
The calculated value of a stat after all relevant modifiers are applied.
_Avoid_: Current value, result value
