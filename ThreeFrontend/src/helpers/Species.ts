import { signal } from "@preact/signals"
import appstate from "../appstate";

const DegToRad = Math.PI / 180.0;
export class Species {
    name = signal("Planta Fortuita " + Date.now());

    //trunkToWood = signal(1);
    height = signal(12);

    nodeDist = signal(0.1);
    nodeDistVar = signal(0.01);

    //https://sites.google.com/view/plant-diversity/
    monopodialFactor = signal(1); //at 0 it is fully dipodial, between 0 and 1 it is anisotomous see https://sites.google.com/site/paleoplant/terminology/branching
    dominanceFactor = signal(0.7);
    auxinsProduction = signal(100);
    auxinsReach = signal(1);
    lateralsPerNode = signal(2);
    lateralRollDeg = signal(0); //opposite = 0, alternate = 180, others are possible as well
    lateralRollDegVar = signal(5);
    lateralPitchDeg = signal(45);
    lateralPitchDegVar = signal(5);

    leafLevel = signal(2);
    leafLength = signal(0.16);
    leafLengthVar = signal(0.01);
    leafRadius = signal(0.04);
    leafRadiusVar = signal(0.01);
    leafGrowthTime = signal(720);
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

    public static Default() {
        const result = new Species();
        result.name.value = "default";
        return result;
    }

    public save() {
        return {
            name: this.name.peek(),
            height: this.height.peek(),

            nodeDist: this.nodeDist.peek(),
            nodeDistVar: this.nodeDistVar.peek(),

            monopodialFactor: this.monopodialFactor.peek(),
            dominanceFactor: this.dominanceFactor.peek(),

            auxinsProduction: this.auxinsProduction.peek(),
            auxinsReach: this.auxinsReach.peek(),

            lateralsPerNode: this.lateralsPerNode.peek(),
            lateralRollDeg: this.lateralRollDeg.peek(),
            lateralRollDegVar: this.lateralRollDegVar.peek(),
            lateralPitchDeg: this.lateralPitchDeg.peek(),
            lateralPitchDegVar: this.lateralPitchDegVar.peek(),

            leafLevel: this.leafLevel.peek(),
            leafLength: this.leafLength.peek(),
            leafLengthVar: this.leafLengthVar.peek(),
            leafRadius: this.leafRadius.peek(),
            leafRadiusVar: this.leafRadiusVar.peek(),
            leafGrowthTime: this.leafGrowthTime.peek(),
            leafGrowthTimeVar: this.leafGrowthTimeVar.peek(),
            leafPitchDeg: this.leafPitchDeg.peek(),
            leafPitchDegVar: this.leafPitchDegVar.peek(),

            petioleLength: this.petioleLength.peek(),
            petioleLengthVar: this.petioleLengthVar.peek(),
            petioleRadius: this.petioleRadius.peek(),
            petioleRadiusVar: this.petioleRadiusVar.peek(),

            rootsDensity: this.rootsDensity.peek(),
            rootsGravitaxis: this.rootsGravitaxis.peek()
        };
    }

    public load(s: any) {
        this.name.value = s.name;
        this.height.value = s.height;
        this.nodeDist.value = s.nodeDist;
        this.nodeDistVar.value = s.nodeDistVar;

        this.monopodialFactor.value = s.monopodialFactor;
        this.dominanceFactor.value = s.dominanceFactor;

        this.auxinsProduction.value = s.auxinsProduction;
        this.auxinsReach.value = s.auxinsReach;

        this.lateralsPerNode.value = s.lateralsPerNode;
        this.lateralRollDeg.value = s.lateralRollDeg;
        this.lateralRollDegVar.value = s.lateralRollDegVar;
        this.lateralPitchDeg.value = s.lateralPitchDeg;
        this.lateralPitchDegVar.value = s.lateralPitchDegVar;

        this.leafLevel.value = s.leafLevel;
        this.leafLength.value = s.leafLength;
        this.leafLengthVar.value = s.leafLengthVar;
        this.leafRadius.value = s.leafRadius;
        this.leafRadiusVar.value = s.leafRadiusVar;
        this.leafGrowthTime.value = s.leafGrowthTime;
        this.leafGrowthTimeVar.value = s.leafGrowthTimeVar;
        this.leafPitchDeg.value = s.leafPitchDeg;
        this.leafPitchDegVar.value = s.leafPitchDegVar;

        this.petioleLength.value = s.petioleLength;
        this.petioleLengthVar.value = s.petioleLengthVar;
        this.petioleRadius.value = s.petioleRadius;
        this.petioleRadiusVar.value = s.petioleRadiusVar;

        this.rootsDensity.value = s.rootsDensity;
        this.rootsGravitaxis.value = s.rootsGravitaxis;
        return this;
    }

    public serialize() {
        return ({
            N: this.name.peek(),

            H: this.height.peek(),
            ND: this.nodeDist.peek(),
            NDv: this.nodeDistVar.peek(),

            BMF: this.monopodialFactor.peek(),
            BDF: this.dominanceFactor.peek(),

            AP: this.auxinsProduction.peek(),
            AR: this.auxinsReach.peek(),

            BLN: this.lateralsPerNode.peek(),
            BR: this.lateralRollDeg.peek() * DegToRad,
            BRv: this.lateralRollDegVar.peek() * DegToRad,
            BP: this.lateralPitchDeg.peek() * DegToRad,
            BPv: this.lateralPitchDegVar.peek() * DegToRad,

            LV: this.leafLevel.peek(),
            LL: this.leafLength.peek(),
            LLv: this.leafLengthVar.peek(),
            LR: this.leafRadius.peek(),
            LRv: this.leafRadiusVar.peek(),
            LGT: this.leafGrowthTime.peek(),
            LGTv: this.leafGrowthTimeVar.peek(),
            LP: this.leafPitchDeg.peek() * DegToRad,
            LPv: this.leafPitchDegVar.peek() * DegToRad,

            PL: this.petioleLength.peek(),
            PLv: this.petioleLengthVar.peek(),
            PR: this.petioleRadius.peek(),
            PRv: this.petioleRadiusVar.peek(),

            RS: 100 - 99.999 * Math.min(1, Math.max(0, this.rootsDensity.peek())), //roots sparsity
            RG: this.rootsGravitaxis.peek()
        });
    }
}