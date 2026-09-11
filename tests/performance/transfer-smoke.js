import http from 'k6/http';
import { check } from 'k6';

const baseUrl =
    __ENV.K6_BASE_URL
    || (
        'http://'
        + '127.0.0.1:5080'
    );

export const options = {
    vus: 1,

    iterations: 5,

    thresholds: {
        checks: [
            'rate==1.0',
        ],

        http_req_failed: [
            'rate==0',
        ],

        http_req_duration: [
            'p(95)<1000',
        ],
    },
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
                        'create-account',
                },
            }
        );

    const passed =
        check(
            response,
            {
                'account creation returns 201':
                    (r) =>
                        r.status === 201,
            }
        );

    if (!passed) {
        throw new Error(
            `Account creation failed: `
            + `${response.status} `
            + `${response.body}`
        );
    }

    return response.json('id');
}

export function setup() {
    const suffix =
        `${Date.now()}-${Math.floor(
            Math.random() * 1000000
        )}`;

    const sourceAccountId =
        createAccount(
            `K6-SRC-${suffix}`,
            'k6 Smoke Source',
            1000.00
        );

    const destinationAccountId =
        createAccount(
            `K6-DST-${suffix}`,
            'k6 Smoke Destination',
            100.00
        );

    return {
        sourceAccountId,
        destinationAccountId,
    };
}

export default function (
    data
) {
    const idempotencyKey =
        crypto.randomUUID();

    const response =
        http.post(
            `${baseUrl}/api/transfers`,
            JSON.stringify({
                sourceAccountId:
                    data.sourceAccountId,

                destinationAccountId:
                    data.destinationAccountId,

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

    check(
        response,
        {
            'transfer returns 201':
                (r) =>
                    r.status === 201,

            'transfer has id':
                (r) => {
                    try {
                        return Boolean(
                            r.json('id')
                        );
                    }
                    catch {
                        return false;
                    }
                },
        }
    );
}
