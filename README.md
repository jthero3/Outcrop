# Outcrop

## Background

This mod adds support for custom objects to be added as custom ROC science. ROC was added as part of the Breaking Ground DLC released some time ago, adding a new science experiment that had to be searched for on each planet surface. Each planet had custom surface features, that could either be picked up by a Kerbal, or tested with the new Science arm added by the same DLC.

Unfortunately, these experiments were hardcoded to only be usable with the Surface features added by the DLC. So while a planet modder could add any of the stock surface features to their planets, they could never implement their own custom one features to better suit their design.

## Implementation

ROC Surface features are pulled from only their specific asset bundles, these prefabs are then fed directly into an array that ROC uses to reference these surface features. So while the structure itself is extensible, the loading of these prefabs was hard coded to only the DLC features.

Outcrop builds custom prefabs that then get added appended to the list that ROC uses to store features. These features can then be used like any other surface feature from stock.

## Example:

I've included an example for now that takes a `.mu` from my `Kerbin Heavy Industries` mod, and makes it a surface feature.

Taking the following example piece by piece:

```
ROC_PREFAB
{
    prefabName        = myFurnace
    modelName         = Furnace
    model             = KerbinHeavyIndustries/Assets/KD-Furnace

    // Auto   - use .mu colliders if present, else build a MeshCollider
    colliderMode      = Auto

    // Name of the child transform to take the collider mesh from.
    // Leave blank to auto-pick the highest-detail mesh. Only set this if you
    // have confirmed the transform name inside the .mu.
    // colliderTransform =

    // Omit `shader` to borrow the one stock ROC prefabs use.
    MATERIAL
    {
        // shader   = KSP/Bumped Specular
        mainTex     = KerbinHeavyIndustries/Assets/KD-Furnace
        bumpMap     = KerbinHeavyIndustries/Assets/KD-Furnace_NRM
        specColor   = 0.15, 0.15, 0.15, 1
        shininess   = 0.08
    }

    autoScanPoints = 8      // per ring, 0 = off
    autoScanRings  = 3
    autoScanLow    = 0.25   // fraction of mesh height
    autoScanHigh   = 0.75
    autoScanInset  = 1.0    // radius multiplier

    colliderConvex    = false
    layer             = Local Scenery
}
```

Each prefab needs a unique `prefabName` to reference later on in the normal ROC config, along with a `modelName`. 

```
    prefabName        = myFurnace
    modelName         = Furnace
```

This is how you reference your custom model. Point it at your model, along with adding a `Material` node. Outcrop looks for colliders present within the mesh, and if they aren't (like with most parallax objects) it will build it using the mesh. Finally, point the material node at your `albedo` and `normal` maps.

NOTE: Currently only tested with stock shaders.

```
    model             = KerbinHeavyIndustries/Assets/KD-Furnace

    // Auto   - use .mu colliders if present, else build a MeshCollider
    colliderMode      = Auto

    // Name of the child transform to take the collider mesh from.
    // Leave blank to auto-pick the highest-detail mesh. Only set this if you
    // have confirmed the transform name inside the .mu.
    // colliderTransform =

    // Omit `shader` to borrow the one stock ROC prefabs use.
    MATERIAL
    {
        // shader   = KSP/Bumped Specular
        mainTex     = KerbinHeavyIndustries/Assets/KD-Furnace
        bumpMap     = KerbinHeavyIndustries/Assets/KD-Furnace_NRM
        specColor   = 0.15, 0.15, 0.15, 1
        shininess   = 0.08
    }
```

Stock ROC uses manually placed points in space, that the scanner arm module looks for to start it's scan. These points are what must be with the 3 meter range that the arms reference. These are all based off of local space for the model, so they can be derived while making the model, or they can be automatically generated with the following. Auto is disabled when custom points are added in the `ROC_DEFINITION`.

I highly recommend using the debug menu to place these points, to make sure that they are reachable by a normal craft.

```
    autoScanPoints = 8      // per ring, 0 = off
    autoScanRings  = 3
    autoScanLow    = 0.25   // fraction of mesh height
    autoScanHigh   = 0.75
    autoScanInset  = 1.0    // radius multiplier

    colliderConvex    = false
    layer             = Local Scenery
```

The following `ROC_DEFINITION` and `EXPERIMENT_DEFINITION` are fully stock. They reference the prefab built above, but can otherwise be fully changed to suit your implementations.

```
ROC_DEFINITION
{
    Mod                  = Outcrop
    Type                 = KerbinSledgeHammer
    displayName          = SledgeHammer
    prefabName           = mySledgeHammer
    modelName            = SledgeHammer
    OrientateUp          = true
    Depth                = 0.3
    CanBeTaken           = true
    Frequency            = 20
    CastShadows          = true
    ReceiveShadows       = true
    CollisionThreshold   = 8
    SmallROC             = true
    RandomDepth          = false
    RandomOrientation    = true
    RandomRotation       = false
    Scale                = 1
	// localSpaceScanPoints = 0, 1.5, -1.5
    CELESTIALBODY
    {
        Name  = Kerbin
        Biome = Shores
    }
}

EXPERIMENT_DEFINITION
{
    id                     = ROCScience_KerbinSledgeHammer
    title                  = SledgeHammer Analysis
    baseValue              = 45
    scienceCap             = 45
    dataScale              = 1
    requireAtmosphere      = False
    situationMask          = 1
    biomeMask              = 0
    RESULTS
    {
        default   = This is a SledgeHammer.
        KerbinSrf = This is a SledgeHammer.
    }
}
```