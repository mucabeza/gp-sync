-- =========================================================================
-- GpSyncQuery.sql — Query base de sincronizacion GP -> Salesforce
-- =========================================================================
-- REGLAS PARA EDITAR ESTE ARCHIVO:
--
-- 1. Debe ser UNA sola sentencia SELECT (no INSERT/UPDATE/DELETE/DROP/etc.,
--    no varias sentencias separadas por ";").
--
-- 2. Debe conservar EXACTAMENTE estos alias de columna en el SELECT
--    (la aplicacion los necesita para armar el registro que va a Salesforce):
--    DocumentDate, SalesPersonID, SalesPerson, SOPNumber, SOPType,
--    ComponentSequence, LineItemSequence, CustomerNumber, CustomerName,
--    BillingCity, ItemNumber, ItemDesc, ItemFamily, Qty, Amount,
--    ItemClassCode, ShippingState, ShippingCity, ShippingZipCode.
--    Se puede cambiar de que tablas/joins/alias salen esos valores, pero no
--    los nombres de columna de salida (los "AS ...").
--
-- 3. Debe usar los parametros @userid, @StartDate, @EndDate (la app se los
--    pasa siempre).
--
-- 4. NO agregar aca: ORDER BY, OFFSET/FETCH, ni condiciones para filtrar
--    por vendedor/cliente/producto/clase de item. Esos 4 filtros opcionales
--    y el ordenamiento/paginado los agrega la aplicacion AUTOMATICAMENTE
--    por fuera de esta query, usando estas columnas de salida:
--      - ItemClassType   -> filtra por ItemClassCode
--      - SalesPersonId   -> filtra por SalesPersonID
--      - CustomerNumber  -> filtra por CustomerNumber
--      - ProductNumber   -> filtra por ItemNumber
--    Si se agregan aca tambien, se va a duplicar el filtro o romper la query.
--
-- 5. La query final que se ejecuta (con los filtros ya agregados) queda
--    registrada en el log de la aplicacion (carpeta logs/) cada vez que
--    corre, por si hace falta ver exactamente que se ejecuto.
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
