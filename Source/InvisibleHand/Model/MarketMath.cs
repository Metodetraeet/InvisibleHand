using System;

namespace InvisibleHand;

//the complete market mathematics in one place 
public static class MarketMath
{
    //marginal price at stock S: (S*/S)^alpha, clamped to [priceMin, priceMax]
    //stock at or below zero reads as infinite scarcity and clamps to priceMax
    public static double PriceRatio(
        double equilibriumStock,
        double stock,
        double alpha,
        double priceMin,
        double priceMax)
    {
        if (equilibriumStock <= 0.0)
        {
            return 1.0;
        }
        if (stock <= 0.0)
        {
            return priceMax;
        }
        return Clamp(Math.Pow(equilibriumStock / stock, alpha), priceMin, priceMax);
    }

    //invert p = p0 * (S*/S)^alpha for S: the stock that explains the price.
    public static double StockForPriceRatio(
        double equilibriumStock,
        double priceRatio,
        double alpha)
    {
        return equilibriumStock * Math.Pow(priceRatio, -1.0 / alpha);
    }

    //closed-form average of the clamped marginal price ratio
    //sell: I(v) = (1 - (1+v)^(1-a))/(a-1)   (a != 1),   ln(1+v)  (a = 1)
    //buy:  I(v) = ((1-v)^(1-a) - 1)/(a-1)   (a != 1),  -ln(1-v)  (a = 1)
    //average of clamp((S*/S')^a, priceMin, priceMax) over S' in [lowStock, highStock]
    public static double AverageClampedCurve(
        double equilibriumStock,
        double lowStock,
        double highStock,
        double alpha,
        double priceMin,
        double priceMax)
    {
        if (highStock - lowStock < 1e-9)
        {
            return PriceRatio(equilibriumStock, lowStock, alpha, priceMin, priceMax);
        }
        //stock thresholds where the raw curve crosses the band edges
        double ceilingStock = equilibriumStock * Math.Pow(priceMax, -1.0 / alpha);
        double floorStock = equilibriumStock * Math.Pow(priceMin, -1.0 / alpha);
        double total = 0.0;
        //scarcity plateau. Everything below ceilingStock trades at the ceiling
        double c2 = Math.Min(highStock, ceilingStock);
        if (c2 > lowStock)
        {
            total += (c2 - lowStock) * priceMax;
        }
        double m1 = Math.Max(lowStock, ceilingStock);
        double m2 = Math.Min(highStock, floorStock);
        if (m2 > m1)
        {
            total += Math.Abs(alpha - 1.0) < 1e-9
                ? equilibriumStock * Math.Log(m2 / m1)
                : Math.Pow(equilibriumStock, alpha)
                    * (Math.Pow(m2, 1.0 - alpha) - Math.Pow(m1, 1.0 - alpha)) / (1.0 - alpha);
        }
        //glut plateau. Everything above floorStock trades at the floor
        double f1 = Math.Max(lowStock, floorStock);
        if (highStock > f1)
        {
            total += (highStock - f1) * priceMin;
        }
        return total / (highStock - lowStock);
    }

    //closingStock: yesterday's closing stock (caller resolves a missing entry to equilibrium).
    //effectiveStock: closing stock plus same-day pending units, so splitting a dump into many deals can't recover flat pricing.
    public static double ExecutionImpactFactor(
        double equilibriumStock,
        double closingStock,
        double effectiveStock,
        double units,
        bool selling,
        double alpha,
        double stockFloorFraction,
        double priceMin,
        double priceMax)
    {
        if (equilibriumStock <= 0.0 || units <= 0.0)
        {
            return 1.0;
        }
        double physicalFloor = equilibriumStock * stockFloorFraction;
        double sClose = Math.Max(closingStock, physicalFloor);
        double s = Math.Max(effectiveStock, physicalFloor);

        double avg;
        if (selling)
        {
            avg = AverageClampedCurve(equilibriumStock, s, s + units, alpha, priceMin, priceMax);
        }
        else
        {
            //a purchase can exceed the market's drainable stock. The curve prices only down to the physical floor
            //this prevents undercharging big buys and should preserve split invariance
            double curveUnits = Math.Min(units, Math.Max(s - physicalFloor, 0.0));
            double excessUnits = units - curveUnits;
            double total = 0.0;
            if (curveUnits > 0.0)
            {
                total += curveUnits * AverageClampedCurve(
                    equilibriumStock, s - curveUnits, s, alpha, priceMin, priceMax);
            }
            if (excessUnits > 0.0)
            {
                total += excessUnits * PriceRatio(
                    equilibriumStock, physicalFloor, alpha, priceMin, priceMax);
            }
            avg = total / units;
        }

        //the caller's price is anchored at yesterday's closing stock, while avg is measured on today's effective stock
        //without this division, splitting a dump into many deals would decrease the price
        double relClose = PriceRatio(equilibriumStock, sClose, alpha, priceMin, priceMax);
        return Clamp(avg, priceMin, priceMax) / relClose;
    }

    //linearized price response used to derive peak gain:
    //pi(t) = K*a*(e^-lambda*t - e^-beta*t)/(beta-lambda):
    //a = alpha/depth, lambda = alpha*(ed+es)/depth, beta = 1/eFold.
    public static double PeakGain(
        double depthDays,
        double alpha,
        double demandElasticity,
        double supplyElasticity,
        double newsEFoldingDays)
    {
        if (depthDays <= 0.0 || newsEFoldingDays <= 0.0)
        {
            return 1e-4;
        }
        double a = alpha / depthDays;
        double lambda = alpha * (demandElasticity + supplyElasticity) / depthDays;
        double beta = 1.0 / newsEFoldingDays;
        if (lambda <= 0.0)
        {
            return Math.Max(a / beta, 1e-4);
        }
        if (Math.Abs(beta - lambda) < 1e-5)
        {
            double ts = 1.0 / beta;
            return Math.Max(a * ts * Math.Exp(-beta * ts), 1e-4);
        }
        double tStar = Math.Log(beta / lambda) / (beta - lambda);
        double g = a * (Math.Exp(-lambda * tStar) - Math.Exp(-beta * tStar)) / (beta - lambda);
        return Math.Max(g, 1e-4);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}