import { IPlantModel } from "src/appstate";

export enum CoordinateSystem { Unknown = 0,
    LH_rightNX_upNY, LH_rightNX_upPY, LH_rightPX_upNY, LH_rightPX_upPY,
    LH_rightNY_upNX, LH_rightNY_upPX, LH_rightPY_upNX, LH_rightPY_upPX,

    LH_rightNX_upNZ, LH_rightNX_upPZ, LH_rightPX_upNZ, LH_rightPX_upPZ,
    LH_rightNZ_upNX, LH_rightNZ_upPX, LH_rightPZ_upNX, LH_rightPZ_upPX,

    LH_rightNY_upNZ, LH_rightNY_upPZ, LH_rightPY_upNZ, LH_rightPY_upPZ,
    LH_rightNZ_upNY, LH_rightNZ_upPY, LH_rightPZ_upNY, LH_rightPZ_upPY,

    RH_rightNX_upNY, RH_rightNX_upPY, RH_rightPX_upNY, RH_rightPX_upPY,
    RH_rightNY_upNX, RH_rightNY_upPX, RH_rightPY_upNX, RH_rightPY_upPX,

    RH_rightNX_upNZ, RH_rightNX_upPZ, RH_rightPX_upNZ, RH_rightPX_upPZ,
    RH_rightNZ_upNX, RH_rightNZ_upPX, RH_rightPZ_upNX, RH_rightPZ_upPX,

    RH_rightNY_upNZ, RH_rightNY_upPZ, RH_rightPY_upNZ, RH_rightPY_upPZ,
    RH_rightNZ_upNY, RH_rightNZ_upPY, RH_rightPZ_upNY, RH_rightPZ_upPY,
}

export function convertCoordsTo_RH_rightPX_upPY(seed: IPlantModel, coords: CoordinateSystem) {
    const { x, y, z } = seed;
    const xyz = (x: number, y: number, z: number): IPlantModel => ({ ...seed, x, y, z });

    switch (coords) {
        // Left-handed
        case CoordinateSystem.LH_rightNX_upNY: return xyz(-x, -y, -z);
        case CoordinateSystem.LH_rightNX_upPY: return xyz(-x,  y,  z);
        case CoordinateSystem.LH_rightPX_upNY: return xyz( x, -y,  z);
        case CoordinateSystem.LH_rightPX_upPY: return xyz( x,  y, -z);

        case CoordinateSystem.LH_rightNY_upNX: return xyz(-y, -x,  z);
        case CoordinateSystem.LH_rightNY_upPX: return xyz(-y,  x, -z);
        case CoordinateSystem.LH_rightPY_upNX: return xyz( y, -x, -z);
        case CoordinateSystem.LH_rightPY_upPX: return xyz( y,  x,  z);


        case CoordinateSystem.LH_rightNX_upNZ: return xyz(-x, -z,  y);
        case CoordinateSystem.LH_rightNX_upPZ: return xyz(-x,  z, -y);
        case CoordinateSystem.LH_rightPX_upNZ: return xyz( x, -z, -y);
        case CoordinateSystem.LH_rightPX_upPZ: return xyz( x,  z,  y);

        case CoordinateSystem.LH_rightNZ_upNX: return xyz(-z, -x, -y);
        case CoordinateSystem.LH_rightNZ_upPX: return xyz(-z,  x,  y);
        case CoordinateSystem.LH_rightPZ_upNX: return xyz( z, -x,  y);
        case CoordinateSystem.LH_rightPZ_upPX: return xyz( z,  x, -y);


        case CoordinateSystem.LH_rightNY_upNZ: return xyz(-y, -z, -x);
        case CoordinateSystem.LH_rightNY_upPZ: return xyz(-y,  z,  x);
        case CoordinateSystem.LH_rightPY_upNZ: return xyz( y, -z,  x);
        case CoordinateSystem.LH_rightPY_upPZ: return xyz( y,  z, -x);

        case CoordinateSystem.LH_rightNZ_upNY: return xyz(-z, -y,  x);
        case CoordinateSystem.LH_rightNZ_upPY: return xyz(-z,  y, -x);
        case CoordinateSystem.LH_rightPZ_upNY: return xyz( z, -y, -x);
        case CoordinateSystem.LH_rightPZ_upPY: return xyz( z,  y,  x);

        // Right-handed
        case CoordinateSystem.RH_rightNX_upNY: return xyz(-x, -y,  z);
        case CoordinateSystem.RH_rightNX_upPY: return xyz(-x,  y, -z);
        case CoordinateSystem.RH_rightPX_upNY: return xyz( x, -y, -z);
        case CoordinateSystem.RH_rightPX_upPY: return xyz( x,  y,  z);

        case CoordinateSystem.RH_rightNY_upNX: return xyz(-y, -x, -z);
        case CoordinateSystem.RH_rightNY_upPX: return xyz(-y,  x,  z);
        case CoordinateSystem.RH_rightPY_upNX: return xyz( y, -x,  z);
        case CoordinateSystem.RH_rightPY_upPX: return xyz( y,  x, -z);


        case CoordinateSystem.RH_rightNX_upNZ: return xyz(-x, -z, -y);
        case CoordinateSystem.RH_rightNX_upPZ: return xyz(-x,  z,  y);
        case CoordinateSystem.RH_rightPX_upNZ: return xyz( x, -z,  y);
        case CoordinateSystem.RH_rightPX_upPZ: return xyz( x,  z, -y);

        case CoordinateSystem.RH_rightNZ_upNX: return xyz(-z, -x,  y);
        case CoordinateSystem.RH_rightNZ_upPX: return xyz(-z,  x, -y);
        case CoordinateSystem.RH_rightPZ_upNX: return xyz( z, -x, -y);
        case CoordinateSystem.RH_rightPZ_upPX: return xyz( z,  x,  y);


        case CoordinateSystem.RH_rightNY_upNZ: return xyz(-y, -z,  x);
        case CoordinateSystem.RH_rightNY_upPZ: return xyz(-y,  z, -x);
        case CoordinateSystem.RH_rightPY_upNZ: return xyz( y, -z, -x);
        case CoordinateSystem.RH_rightPY_upPZ: return xyz( y,  z,  x);

        case CoordinateSystem.RH_rightNZ_upNY: return xyz(-z, -y, -x);
        case CoordinateSystem.RH_rightNZ_upPY: return xyz(-z,  y,  x);
        case CoordinateSystem.RH_rightPZ_upNY: return xyz( z, -y,  x);
        case CoordinateSystem.RH_rightPZ_upPY: return xyz( z,  y, -x);

        case CoordinateSystem.Unknown:
        default:
            throw new Error(`Unknown coordinate system: ${coords}`);
    }
}