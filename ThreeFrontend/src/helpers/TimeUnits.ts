export const encodeTime = (t: number) => {
    if (t == 1) return  "1 hour";
    else if (t < 24) return `${t} hours`;
    else if (t == 24) return "1 day";
    else if (t < 168) return `${Math.round(t / 24)} days`;
    else if (t == 168) return `1 week`;
    else if (t < 720) return `${Math.round(t / 168)} weeks`;
    else if (t == 720) return "1 month";
    else if (t < 8760) return `${Math.round(t / 720)} months`;
    else if (t == 8760) return "1 year";
    else return `${Math.round(t / 8760)} years`;
}