-- =========================================================================
-- GpSyncQuery.sql — Base query for GP -> Salesforce synchronization
-- =========================================================================
-- RULES FOR EDITING THIS FILE:
--
-- 1. It must be a SINGLE SELECT statement (no INSERT/UPDATE/DELETE/DROP/etc.,
--    no multiple statements separated by ";").
--
-- 2. It must keep EXACTLY these column aliases in the SELECT
--    (the application needs them to build the record sent to Salesforce):
--    DocumentDate, SalesPersonID, SalesPerson, SOPNumber, SOPType,
--    ComponentSequence, LineItemSequence, CustomerNumber, CustomerName,
--    BillingCity, ItemNumber, ItemDesc, ItemFamily, Qty, Amount,
--    ItemClassCode, ShippingState, ShippingCity, ShippingAddress,
--    ShippingZipCode, ShippingCountry.
--    You may change which tables/joins/aliases those values come from, but not
--    the output column names (the "AS ...").
--
-- 3. It must use the parameters @userid, @StartDate, @EndDate (the app always
--    passes them).
--
-- 4. DO NOT add here: ORDER BY, OFFSET/FETCH, or conditions to filter
--    by salesperson/customer/product/item class. Those 4 optional filters
--    and the ordering/paging are added by the application AUTOMATICALLY
--    outside this query, using these output columns:
--      - ItemClassType   -> filters by ItemClassCode
--      - SalesPersonId   -> filters by SalesPersonID
--      - CustomerNumber  -> filters by CustomerNumber
--      - ProductNumber   -> filters by ItemNumber
--    If they are added here too, the filter will be duplicated or the query
--    will break.
--
-- 5. The final query that is executed (with the filters already added) is
--    recorded in the application log (logs/ folder) every time it runs,
--    in case you need to see exactly what was executed.
-- =========================================================================

SELECT DISTINCT
    H.DOCDATE  AS DocumentDate,
    CASE ISNULL(SH.CS_Shipto, 1)
        WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
        WHEN 2 THEN COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
        ELSE COALESCE(ST_NASTATE.SLPRSNID, RM2.SLPRSNID)
    END AS SalesPersonID,
    LTRIM(RTRIM(SAL.SPRSNSLN)) + ', ' + LTRIM(RTRIM(SAL.SLPRSNFN)) AS SalesPerson,
    H.SOPNUMBE AS SOPNumber,
    H.SOPTYPE  AS SOPType,
    L.CMPNTSEQ AS ComponentSequence,
    L.LNITMSEQ AS LineItemSequence,
    H.CUSTNMBR AS CustomerNumber,
    RM1.CUSTNAME AS CustomerName,
    RM1.CITY   AS BillingCity,
    L.ITEMNMBR AS ItemNumber,
    L.ITEMDESC AS ItemDesc,
    E.ITMCLSDC AS ItemFamily,
    L.QUANTITY AS Qty,
    L.QUANTITY * L.UNITPRCE AS Amount,
    I.ITMCLSCD AS ItemClassCode,
    H.STATE    AS ShippingState,
    H.CITY     AS ShippingCity,
    H.ADDRESS1 AS ShippingAddress,
    H.ZIPCODE  AS ShippingZipCode,
    H.COUNTRY  AS ShippingCountry
FROM [PD].[dbo].[SOP30300] L
    LEFT JOIN [PD].[dbo].[SOP30200] H
        ON H.SOPTYPE = L.SOPTYPE
        AND H.SOPNUMBE = L.SOPNUMBE

    LEFT JOIN [PD].[dbo].[RM00101] RM1
        ON RM1.CUSTNMBR = H.CUSTNMBR

    LEFT JOIN [PD].[dbo].[IV00101] I
        ON L.ITEMNMBR = I.ITEMNMBR

    LEFT JOIN [PD].[dbo].[IV40400] E
        ON E.ITMCLSCD = I.ITMCLSCD

    LEFT JOIN [PD].[dbo].[RM00102] RM2
        ON RM2.CUSTNMBR = H.CUSTNMBR
        AND RM2.ADRSCODE = H.PRSTADCD

    LEFT JOIN [PD].[dbo].[CS_SHIPT] SH
        ON SH.CUSTNMBR = RM1.CUSTNMBR

    LEFT JOIN [PD].[dbo].[CS_STATE] ST_STATE
        ON ST_STATE.STATE = H.STATE
        AND LTRIM(RTRIM(ST_STATE.CITY)) = ''

    LEFT JOIN [PD].[dbo].[CS_STATE] ST_CITY
        ON ST_CITY.STATE = H.STATE
        AND ST_CITY.CITY = H.CITY
        AND LTRIM(RTRIM(ST_CITY.CITY)) <> ''

    LEFT JOIN [PD].[dbo].[CS_NASTATE] ST_NASTATE
        ON ST_NASTATE.STATE = H.STATE

    LEFT JOIN [PD].[dbo].[RM00301] SAL
        ON SAL.SLPRSNID = CASE ISNULL(SH.CS_Shipto, 1)
            WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
            WHEN 2 THEN COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
            ELSE COALESCE(ST_NASTATE.SLPRSNID, RM2.SLPRSNID)
        END

WHERE
    CASE ISNULL(SH.CS_Shipto, 1)
        WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
        WHEN 2 THEN COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
        ELSE COALESCE(ST_NASTATE.SLPRSNID, RM2.SLPRSNID)
    END IS NOT NULL
    AND CASE ISNULL(SH.CS_Shipto, 1)
        WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
        WHEN 2 THEN COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
        ELSE COALESCE(ST_NASTATE.SLPRSNID, RM2.SLPRSNID)
    END <> ''
    AND H.SOPTYPE IN (3, 4)
    AND [VOIDSTTS] = 0
    AND L.QUANTITY <> 0
    AND (
        (
            ((SELECT linked FROM CSUSRep WHERE CS_User = @userid) = 0)
            AND
            (
                ((SELECT COUNT(*) FROM CS_SRepList(@userid)) = 0)
                OR
                (CASE ISNULL(SH.CS_Shipto, 1)
                    WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
                    WHEN 2 THEN COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
                    ELSE COALESCE(ST_NASTATE.SLPRSNID, RM2.SLPRSNID)
                END IN (SELECT CSSREP FROM CS_SRepList(@userid)))
            )
        )
        OR
        (
            ((SELECT linked FROM CSUSRep WHERE CS_User = @userid) = 1)
            AND
            (RM1.SLPRSNID = (SELECT CS_SalesRep FROM CSUSRep WHERE CS_User = @userid))
        )
    )
    AND (([DOCDATE] >= @StartDate AND [DOCDATE] < @EndDate) OR (L.DEX_ROW_TS >= @StartDate AND L.DEX_ROW_TS < @EndDate))
