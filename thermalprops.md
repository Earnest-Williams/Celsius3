# Thermal data definitions.

ThermalProps  :  just a class. 


StuffThermalProperties : DefModExtension
    - stores stuff-related data:
        - volumetricHeatCapacity
        - insulationFactor

ThingThermalProperties : DefModExtension
    - stores thing-related data:
        - heatCapacity
        - volume
        - insulation (defaults to 1)
        - airflowWhenOpen
        - airflow 
    
TerrainThermalProperties : ThingThermalProperties
    - adds PhaseTransition data:
        - direction: freeze/melt
        - turnsInto TerrainDef ref
        - transition temperature
        

## Data defined in mods (Celsius + CelsiusPatches):

### TerrainDef:

ModExtension class Celsius.TerrainThermalProperties

 - heatCapacity
 - insulation
 - phaseTransition  : one of { Melt, Freeze }
 - transitionTemperature
  
### ThingDef:

ModExtension class Celsius.ThingThermalProperties
 
 - heatCapacity     : sometimes overriden to the tune of 3-17 for things,
                      650-1100 for terrains, 500 for mineable (!!). Ignored for stuffables.
 - insulation       : default 1.0 typical 3-9
 - volume           : under 1.0 everywhere, mostly 0.01-ish except 0.5 for minable and 10 in a haywall.??
 - airflow          : normalized 0.0 - 1.0; default NaN? 
 - airflowWhenOpen
  
ModExtension class Celsius.StuffThermalProperties

 - volumetricHeatCapacity
 - insulationFactor
 
### statBases of ThingDefs

 - Celsius_IgnitionTemperature in statBases
 - Celsius_IgnitionTemperature in stuffProps.statOffsets (what is this?) 

 
both are the same for some reason. TODO: document why and what for there are two values

