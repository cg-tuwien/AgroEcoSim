import { signal } from "@preact/signals"
import appstate from "../appstate";
import { radToDeg } from "three/src/math/MathUtils";
import BinaryReader from "./BinaryReader";
import * as THREE from 'three';

const DegToRad = Math.PI / 180.0;
const RadToDeg = 180.0 / Math.PI;

export class Species {
    name = signal("Planta Fortuita " + Date.now());
    aka = signal("");
    behaviorIndex = signal(0);

    //trunkToWood = signal(1);
    height = signal(12);

    nodeDistance = signal(0.04);
    nodeDistanceVar = signal(0.01);

    //https://sites.google.com/view/plant-diversity/
    monopodialFactor = signal(1); //at 0 it is fully dipodial, between 0 and 1 it is anisotomous see https://sites.google.com/site/paleoplant/terminology/branching
    dominanceFactor = signal(0.7);
    auxinsProduction = signal(40);
    auxinsReach = signal(1);
    lateralsPerNode = signal(2);
    lateralRollDeg = signal(0); //opposite = 0, alternate = 180, others are possible as well
    lateralRollDegVar = signal(5);
    lateralPitchDeg = signal(45);
    lateralPitchDegVar = signal(5);
    twigsBending = signal(0.5);
    twigsBendingApical = signal(0.02);
    bendingByLevel = signal(1);
    shootsGravitaxis = signal(0.2);

    woodGrowthTime = signal(100);
    woodGrowthTimeVar = signal(10);

    //leafLevel = signal(2);
    leafLength = signal(0.12);
    leafLengthVar = signal(0.02);
    leafRadius = signal(0.04);
    leafRadiusVar = signal(0.01);
    leafGrowthTime = signal(480);
    leafGrowthTimeVar = signal(120);
    leafPitchDeg = signal(20);
    leafPitchDegVar = signal(5);

    petioleLength = signal(0.05);
    petioleLengthVar = signal(0.01);
    petioleRadius = signal(0.0015);
    petioleRadiusVar = signal(0.0005);
    // RootLengthGrowthPerH = 0.023148148f,
    // RootRadiusGrowthPerH = 0.00297619f,

    rootsDensity = signal(0.5);
    rootsGravitaxis = signal(0.2);

    includeInRndGen = signal(true);

    leafMorphology: any;
    leafPhenology: any;


    leafGeometry: THREE.BufferGeometry | undefined = undefined;

    public static Default() {
        const result = new Species();
        result.name.value = "default";
        return result;
    }

    public save() {
        return {
            name: this.name.peek(),
            aka: this.aka.peek(),
            behavior: this.behaviorIndex.peek(),
            height: this.height.peek(),

            nodeDistance: this.nodeDistance.peek(),
            nodeDistanceVar: this.nodeDistanceVar.peek(),

            monopodialFactor: this.monopodialFactor.peek(),
            dominanceFactor: this.dominanceFactor.peek(),

            auxinsProduction: this.auxinsProduction.peek(),
            auxinsReach: this.auxinsReach.peek(),

            lateralsPerNode: this.lateralsPerNode.peek(),
            lateralRoll: this.lateralRollDeg.peek() * DegToRad,
            lateralRollVar: this.lateralRollDegVar.peek() * DegToRad,
            lateralPitch: this.lateralPitchDeg.peek() * DegToRad,
            lateralPitchVar: this.lateralPitchDegVar.peek() * DegToRad,

            twigsBending: this.twigsBending.peek(),
            twigsBendingApical: this.twigsBendingApical.peek(),
            bendingByLevel: this.bendingByLevel.peek(),
            shootsGravitaxis: this.shootsGravitaxis.peek(),

            woodGrowthTime: this.woodGrowthTime.peek(),
            woodGrowthTimeVar: this.woodGrowthTimeVar.peek(),

            //leafLevel: this.leafLevel.peek(),
            leafLength: this.leafLength.peek(),
            leafLengthVar: this.leafLengthVar.peek(),
            leafRadius: this.leafRadius.peek(),
            leafRadiusVar: this.leafRadiusVar.peek(),
            leafGrowthTime: this.leafGrowthTime.peek(),
            leafGrowthTimeVar: this.leafGrowthTimeVar.peek(),
            leafPitch: this.leafPitchDeg.peek() * DegToRad,
            leafPitchVar: this.leafPitchDegVar.peek() * DegToRad,
            leafMorphology: this.leafMorphology,
            leafPhenology: this.leafPhenology,

            petioleLength: this.petioleLength.peek(),
            petioleLengthVar: this.petioleLengthVar.peek(),
            petioleRadius: this.petioleRadius.peek(),
            petioleRadiusVar: this.petioleRadiusVar.peek(),

            rootsDensity: this.rootsDensity.peek(),
            rootsGravitaxis: this.rootsGravitaxis.peek(),

            includeInRndGen: this.includeInRndGen.peek(),
        };
    }

    public load(s: any) {
        this.name.value = s.name;
        this.aka.value = s.aka;
        this.behaviorIndex.value = s.behavior;
        this.height.value = s.height;
        this.nodeDistance.value = s.nodeDistance;
        this.nodeDistanceVar.value = s.nodeDistanceVar;

        this.monopodialFactor.value = s.monopodialFactor;
        this.dominanceFactor.value = s.dominanceFactor;

        this.auxinsProduction.value = s.auxinsProduction;
        this.auxinsReach.value = s.auxinsReach;

        this.lateralsPerNode.value = s.lateralsPerNode;
        this.lateralRollDeg.value = s.lateralRoll * RadToDeg;
        this.lateralRollDegVar.value = s.lateralRollVar * RadToDeg;
        this.lateralPitchDeg.value = s.lateralPitch * RadToDeg;
        this.lateralPitchDegVar.value = s.lateralPitchVar * RadToDeg;

        this.twigsBending.value = s.twigsBending;
        this.twigsBendingApical.value = s.twigsBendingApical;
        this.bendingByLevel.value = s.twigsBendingLevel;
        this.shootsGravitaxis.value = s.shootsGravitaxis;

        this.woodGrowthTime.value = s.woodGrowthTime;
        this.woodGrowthTimeVar.value = s.woodGrowthTimeVar;

        //this.leafLevel.value = s.leafLevel;
        this.leafLength.value = s.leafLength;
        this.leafLengthVar.value = s.leafLengthVar;
        this.leafRadius.value = s.leafRadius;
        this.leafRadiusVar.value = s.leafRadiusVar;
        this.leafGrowthTime.value = s.leafGrowthTime;
        this.leafGrowthTimeVar.value = s.leafGrowthTimeVar;
        this.leafPitchDeg.value = s.leafPitch * RadToDeg;
        this.leafPitchDegVar.value = s.leafPitchVar * RadToDeg;
        this.leafMorphology = s.leafMorphology;
        this.leafPhenology = s.leafPhenology;

        this.petioleLength.value = s.petioleLength;
        this.petioleLengthVar.value = s.petioleLengthVar;
        this.petioleRadius.value = s.petioleRadius;
        this.petioleRadiusVar.value = s.petioleRadiusVar;

        this.rootsDensity.value = s.rootsDensity;
        this.rootsGravitaxis.value = s.rootsGravitaxis;

        this.includeInRndGen.value = s.hasOwnProperty('includeInRndGen') ? s.includeInRndGen : true;
        return this;
    }

    public serialize() {
        return {
            Name: this.name.peek(),
            Aka: this.aka.peek(),
            Behavior: this.behaviorIndex.peek(),

            Height: this.height.peek(),
            NodeDistance: this.nodeDistance.peek(),
            NodeDistanceVar: this.nodeDistanceVar.peek(),

            MonopodialFactor: this.monopodialFactor.peek(),
            DominanceFactor: this.dominanceFactor.peek(),

            AuxinsProduction: this.auxinsProduction.peek(),
            AusinsReach: this.auxinsReach.peek(),

            LateralsPerNode: this.lateralsPerNode.peek(),
            LateralRoll: this.lateralRollDeg.peek() * DegToRad,
            LateralRollVar: this.lateralRollDegVar.peek() * DegToRad,
            LateralPitch: this.lateralPitchDeg.peek() * DegToRad,
            LateralPitchVar: this.lateralPitchDegVar.peek() * DegToRad,

            TwigsBending: this.twigsBending.peek(),
            TwigsBendingLevel: this.bendingByLevel.peek(),
            TwigsBendingApical: 1.0 - this.twigsBendingApical.peek(),
            ShootsGravitaxis: this.shootsGravitaxis.peek(),

            WoodGrowthTime: this.woodGrowthTime.peek() * 24,
            WoodGrowthTimeVar: this.woodGrowthTimeVar.peek() * 24,

            //LeafLevel: this.leafLevel.peek(),
            LeafLength: this.leafLength.peek(),
            LeafLengthVar: this.leafLengthVar.peek(),
            LeafRadius: this.leafRadius.peek(),
            LeafRadiusVar: this.leafRadiusVar.peek(),
            LeafGrowthTime: this.leafGrowthTime.peek(),
            LeafGrowthTimeVar: this.leafGrowthTimeVar.peek(),
            LeafPitch: this.leafPitchDeg.peek() * DegToRad,
            LeafPitchVar: this.leafPitchDegVar.peek() * DegToRad,
            LeafMorphology: this.leafMorphology,
            LeafPhenology: this.leafPhenology,

            PetioleLength: this.petioleLength.peek(),
            PetioleLengthVar: this.petioleLengthVar.peek(),
            PetioleRadius: this.petioleRadius.peek(),
            PetioleRadiusVar: this.petioleRadiusVar.peek(),

            RootsSparsity: 100 - 99.999 * Math.min(1, Math.max(0, this.rootsDensity.peek())), //roots sparsity
            RootsGravitaxis: this.rootsGravitaxis.peek(),

            IncludeInRndGen: this.includeInRndGen.peek(),
        };
    }

    loadLeafGeometry(binaryInput: ArrayBuffer): any {
        const reader = new BinaryReader(new Uint8Array(binaryInput));
        const verticesCount = reader.readInt32();
        const coordsCount = verticesCount * 3;
        const vertices = new Float32Array(coordsCount);
        for(let v = 0; v < coordsCount; ++v)
            vertices[v] = reader.readFloat32();

        const indicesCount = reader.readInt32();
        const indices: number[] = [];
        for(let f = 0; f < indicesCount; ++f)
            indices.push(reader.readInt32());

        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
        geometry.setIndex(indices);
        geometry.computeVertexNormals();
        geometry.computeBoundingBox();
        geometry.computeBoundingSphere();

        this.leafGeometry = geometry;
    }
}