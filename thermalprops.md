# Thermal data definitions.

ThermalProps  :  just a class. 


StuffThermalProperties : DefModExtension
    - stores stuff-related data:
        - volumetricHeatCapacity
        - insulationFactor

ThingThermalProperties : DefModExtension
    - stores thing-related data:
        - heatCapacity  for non-stuffables
        - volume        for stuffables
        - insulation (defaults to 1)
        - airflowWhenOpen
        - airflow 
    
TerrainThermalProperties : DefModExtension
        - heatCapacity
        - insulation
        - direction: freeze/melt
        - turnsInto TerrainDef ref
        - transition temperature
        

## Data defined in mods (Celsius + CelsiusPatches):

Garwel note:

	Since the thermodynamics system implemented in Celsius is a simplification
	of the one implemented in the universe containing Earth most of the
	following stats are presented without use of SI units. The units most
	similar to those on Earth are listed below for making it easier for
	translators to figure out the most suitable thermal engineering vocabulary
	in their languages however:
	 * Volumetric heat capacity: J/(K·m³)
	 * Air heat capacity: J/K

### TerrainDef:

ModExtension class Celsius.TerrainThermalProperties

 - heatCapacity    : 100-1100 for terrains . values around 100 repr. water
 - insulation      : 3-6 waters 10 lava
 - phaseTransition : one of { Melt, Freeze, None }. None is used to disable inherited transition
 - transitionTemperature : in celsius
  
### ThingDef:

#### ModExtension class Celsius.ThingThermalProperties
 
- heatCapacity     : sometimes overriden to the tune of 3-17 for things,
                    500 for mineable (!!). Ignored for stuffables.
- insulation       : default 1.0 typical 3-9
- volume           : under 1.0 everywhere, mostly 0.01-ish except 0.5 for minable and 10 in a haywall.??
- airflow          : normalized 0.0 - 1.0; default 0
- airflowWhenOpen
  
#### ModExtension class Celsius.StuffThermalProperties

 - volumetricHeatCapacity : 500 (wood, plastic), 1000-ish (stone) 1250 (steel)
 - insulationFactor  : normalized 0.0 - 1.0


## ThermalProps

    is what the model uses for cells. 

in before using airflow value :

float GetInsulationWithAirflow(float insulation, float airflow) => Mathf.Lerp(insulation, 1, airflow);
this.insulation = TemperatureUtility.GetInsulationWithAirflow(insulation, airflow);

 this actually increases the resulting insulation value .. counterintuitive heh

Stuff properties are applied in a more or less straightforward way:

heatCapacity = Mathf.Lerp(1, stuffProps.volumetricHeatCapacity, volume)
insulation = insulation * stuffProps.insulationFactor

    additional params:

conductivity = Mathf.Pow(Settings.ConductivityPowerBase, insulation);

heatflow = heatCapacity * conductivity / Settings.ConvectionConductivityEffect;

    - heatflow

### Air is the default case for cells otherwise left undefined:
 - heatCapacity = 1.0
 - volume       = 1.0
 - airflow      = 1.0
 - isAir        = true // why this is even needed?



# Freeze/Melt support

1. Phase transitions are defined as above or they don't happen, no exceptions.
2. Phase transition of type None is not happening either, temperature is ignored.
3. turnsInto contains target TerrainDef's defName
4. Reserved turnsInto of CelsiusFlop means "whatever terrain was there before"
    - reserved for Celsius-internal IceNine terrain.
    - expects the target terrain in terrainFlops layer on melt.
    - thus should not be generated into maps as there is no way to add to terrainFlops
      on generation.
5. When anything with turnsInto of IceNine freezes, it gets noted in terrainFlops.
6. When IceNine melts, replacement terrain is fetched from terrainFlops.
7. Setting in settings disables only freezing into IceNine.

 
### statBases of ThingDefs

 - Celsius_IgnitionTemperature in statBases
 - Celsius_IgnitionTemperature in stuffProps.statOffsets (what is this?) 


 
both are the same for some reason. TODO: document why and what for there are two values

