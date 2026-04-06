## Backward-compatibility shim.  New code and new .tres files should use ResItem.
## This file exists so that any lingering ResWeapon references (e.g. in old .tres
## files not yet migrated) continue to load without errors.
class_name ResWeapon extends ResItem
