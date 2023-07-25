export const encodeTime = (t: number) => {
    if (t == 1) return  "1 hour";
    else if (t < 24) return `${t} hours`;
    else if (t == 24) return "1 day";
    else if (t < 168) { const result = Math.round(t / 24); return `${result} day${result == 1 ? '' : 's'}`; }
    else if (t == 168) return `1 week`;
    else if (t < 720) { const result = Math.round(t / 168); return `${result} week${result == 1 ? '' : 's'}`; }
    else if (t == 720) return "1 month";
    else if (t < 8760) { const result = Math.round(t / 720); return `${result} month${result == 1 ? '' : 's'}`;}
    else if (t == 8760) return "1 year";
    else { const result = Math.round(t / 8760); return `${result} year${result} month${result == 1 ? '' : 's'}`;}
}