import { signal } from "@preact/signals"
import appstate from "../appstate";

const DegToRad = Math.PI / 180.0;
export class Species {
    name = signal("Planta Fortuita " + Date.now());

    //trunkToWood = signal(1);
    height = signal(12);

    //https://sites.google.com/view/plant-diversity/
    monopodialFactor = signal(1); //at 0 it is fully dipodial, between 0 and 1 it is anisotomous see https://sites.google.com/site/paleoplant/terminology/branching
    lateralsPerNode = signal(2);
    lateralAngleDeg = signal(0); //opposite = 0, alternate = 180, others are possible as well


    leafLength = signal(0.2);
    leafRadius = signal(0.04);
    leafGrowthTime = signal(720);

    petioleLength = signal(0.05);
    petioleRadius = signal(0.007);
    // RootLengthGrowthPerH = 0.023148148f,
    // RootRadiusGrowthPerH = 0.00297619f,



    public static Default() {
        const result = new Species();
        result.name.value = "default";
        return result;
    }

    public save() {
        return {
            name: this.name.peek(),
            height: this.height.peek(),

            monopodialFactor: this.monopodialFactor.peek(),
            lateralsPerNode: this.lateralsPerNode.peek(),
            lateralAngleDeg: this.lateralAngleDeg.peek(),

            leafLength: this.leafLength.peek(),
            leafRadius: this.leafRadius.peek(),
            leafGrowthTime: this.leafGrowthTime.peek(),

            petioleLength: this.petioleLength.peek(),
            petioleRadius: this.petioleRadius.peek(),


        };
    }

    public load(s: any) {
        this.name.value = s.name;

        this.monopodialFactor.value = s.monopodialFactor;
        this.lateralsPerNode.value = s.lateralsPerNode;
        this.lateralAngleDeg.value = s.lateralAngleDeg;

        this.leafLength.value = s.leafLength;
        this.leafRadius.value = s.leafRadius;
        this.leafGrowthTime.value = s.leafGrowthTime;

        this.petioleLength.value = s.petioleLength;
        this.petioleRadius.value = s.petioleRadius;

        this.height.value = s.height;
        return this;
    }

    public serialize() {
        return ({
            N: this.name.peek(),

            H: this.height.peek(),

            BMF: this.monopodialFactor.peek(),
            BLN: this.lateralsPerNode.peek(),
            BLA: this.lateralAngleDeg.peek() * DegToRad,

            LL: this.leafLength.peek(),
            LR: this.leafRadius.peek(),
            LGT: this.leafGrowthTime.peek(),

            PL: this.petioleLength.peek(),
            PR: this.petioleRadius.peek(),
        });
    }
}