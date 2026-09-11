import http from 'k6/http';
import { check } from 'k6';
import {
    Counter,
    Rate,
    Trend,
} from 'k6/metrics';

const baseUrl =
    __ENV.K6_BASE_URL
    || (
        'http://'
        + '127.0.0.1:5080'
    );

const virtualUsers =
    Number(
        __ENV.FLUXPAY_K6_VUS
        || 10
    );

const testDuration =
    __ENV.FLUXPAY_K6_DURATION
    || '30s';

const transferDuration =
    new Trend(
        'transfer_duration',
        true
    );

const transferFailures =
    new Rate(
        'transfer_failures'
    );

const transferSuccesses =
    new Counter(
        'transfer_successes'
    );

export const options = {
    scenarios: {
        transfer_baseline: {
            executor:
                'constant-vus',

            vus:
                virtualUsers,

            duration:
                testDuration,

            gracefulStop:
                '10s',
        },
    },

    thresholds: {
        transfer_failures: [
            'rate==0',
        ],

        transfer_duration: [
            'p(95)<1500',
            'p(99)<3000',
        ],
    },

    summaryTrendStats: [
        'avg',
        'med',
        'min',
        'max',
        'p(90)',
        'p(95)',
        'p(99)',
    ],
};

function createAccount(
    accountNumber,
    ownerName,
    initialBalance
) {
    const response =
        http.post(
            `${baseUrl}/api/accounts`,
            JSON.stringify({
                accountNumber,
                ownerName,
                initialBalance,
            }),
            {
                headers: {
                    'Content-Type':
                        'application/json',
                },

                tags: {
                    operation:
                        'setup-create-account',
                },
            }
        );

    const successful =
        check(
            response,
            {
                'setup account creation returns 201':
                    (r) =>
                        r.status === 201,
            }
        );

    if (!successful) {
        throw new Error(
            `Account creation failed: `
            + `${response.status} `
            + `${response.body}`
        );
    }

    return response.json(
        'id'
    );
}

export function setup() {
    const runId =
        `${Date.now()}-`
        + `${Math.floor(
            Math.random()
            * 1000000
        )}`;

    const accounts =
        [];

    for (
        let index = 1;
        index <= virtualUsers;
        index++
    ) {
        const sourceAccountId =
            createAccount(
                `K6-BSRC-${runId}-${index}`,
                `k6 Baseline Source ${index}`,
                1000000.00
            );

        const destinationAccountId =
            createAccount(
                `K6-BDST-${runId}-${index}`,
                `k6 Baseline Destination ${index}`,
                100.00
            );

        accounts.push({
            sourceAccountId,
            destinationAccountId,
        });
    }

    return {
        accounts,
    };
}

export default function (
    data
) {
    const accountIndex =
        (__VU - 1)
        % data.accounts.length;

    const accountPair =
        data.accounts[
            accountIndex
        ];

    const idempotencyKey =
        crypto.randomUUID();

    const response =
        http.post(
            `${baseUrl}/api/transfers`,
            JSON.stringify({
                sourceAccountId:
                    accountPair
                        .sourceAccountId,

                destinationAccountId:
                    accountPair
                        .destinationAccountId,

                amount:
                    1.00,
            }),
            {
                headers: {
                    'Content-Type':
                        'application/json',

                    'Idempotency-Key':
                        idempotencyKey,
                },

                tags: {
                    operation:
                        'execute-transfer',
                },
            }
        );

    transferDuration.add(
        response.timings.duration
    );

    let hasTransferId =
        false;

    if (response.status === 201) {
        try {
            hasTransferId =
                Boolean(
                    response.json(
                        'id'
                    )
                );
        }
        catch {
            hasTransferId =
                false;
        }
    }

    const successful =
        response.status === 201
        && hasTransferId;

    transferFailures.add(
        !successful
    );

    if (successful) {
        transferSuccesses.add(
            1
        );
    }

    check(
        response,
        {
            'transfer returns 201':
                () =>
                    response.status
                    === 201,

            'transfer response has id':
                () =>
                    hasTransferId,
        }
    );
}
