import { signal } from "@preact/signals"
import appstate from "../appstate";

export class Species {
    name = signal("Planta Fortuita " + Date.now());

    //trunkToWood = signal(1);

    leafLength = signal(0.2);
    leafRadius = signal(0.04);
    leafGrowthTime = signal(720);

    petioleLength = signal(0.05);
    petioleRadius = signal(0.007);
    // RootLengthGrowthPerH = 0.023148148f,
    // RootRadiusGrowthPerH = 0.00297619f,

    height = signal(12);

    public static Default() {
        const result = new Species();
        result.name.value = "default";
        return result;
    }

    public save() {
        return {
            name: this.name.peek(),
            leafLength: this.leafLength.peek(),
            leafRadius: this.leafRadius.peek(),
            leafGrowthTime: this.leafGrowthTime.peek(),

            petioleLength: this.petioleLength.peek(),
            petioleRadius: this.petioleRadius.peek(),

            height: this.height.peek(),
        };
    }

    public load(s: any) {
        this.name.value = s.name;

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

            LL: this.leafLength.peek(),
            LR: this.leafRadius.peek(),
            LGT: this.leafGrowthTime.peek(),

            PL: this.petioleLength.peek(),
            PR: this.petioleRadius.peek(),
        });
    }
}